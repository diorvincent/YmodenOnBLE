using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BLE_APPLY;

namespace YmodenOnBluetoolthLE
{
	/*
*********************************************************************************************************
*	                                   Ymodem文件传输协议介绍
*********************************************************************************************************
*/
	/*
	第1阶段： 同步
		从机给数据发送同步字符 C

	第2阶段：发送第1帧数据，包含文件名和文件大小
		主机发送：
		---------------------------------------------------------------------------------------
		| SOH  |  序号 - 0x00 |  序号取反 - 0xff | 128字节数据，含文件名和文件大小字符串|CRC0 CRC1|
		|-------------------------------------------------------------------------------------|
		从机接收：
		接收成功回复ACK和CRC16，接收失败（校验错误，序号有误）继续回复字符C，超过一定错误次数，回复两个CA，终止传输。

	第3阶段：数据传输
		主机发送：
		---------------------------------------------------------------------------------------
		| SOH/STX  |  从0x01开始序号  |  序号取反 | 128字节或者1024字节                |CRC0 CRC1|
		|-------------------------------------------------------------------------------------|
		从机接收：
		接收成功回复ACK，接收失败（校验错误，序号有误）或者用户处理失败继续回复字符C，超过一定错误次数，回复两个CA，终止传输。

	第4阶段：结束帧
		主机发送：发送EOT结束传输。
		从机接收：回复ACK。

	第5阶段：空帧，结束通话
		主机发送：一帧空数据。
		从机接收：回复ACK。
	*/
	class Ymodem
	{
        #region global mark bit define
        byte SOH = 0x01;  /* start of 128-byte data packet */
		byte STX = 0x02;  /* start of 1024-byte data packet */
		byte EOT = 0x04;  /* end of transmission */
		byte ACK = 0x06;  /* acknowledge */
		byte NAK = 0x15;  /* negative acknowledge */
		byte CA = 0x18;  /* two of these in succession aborts transfer */
		byte CRC16 = 0x43;  /* 'C' == 0x43, request 16-bit CRC */
		byte ABORT1 = 0x41;  /* 'A' == 0x41, abort by user */
		byte ABORT2 = 0x61;  /* 'a' == 0x61, abort by user */

		byte PACKET_SEQNO_INDEX = 1;
		byte PACKET_SEQNO_COMP_INDEX = 2;
		byte PACKET_HEADER = 3;
		byte PACKET_TRAILER = 2;
		byte PACKET_OVERHEAD = 5; // PACKET_HEADER + PACKET_TRAILER;
		byte PACKET_SIZE = 128;
		ushort PACKET_1K_SIZE = 1024;
		ushort FILE_NAME_LENGTH = 256;
		byte FILE_SIZE_LENGTH = 16;
		uint NAK_TIMEOUT = 0x100000;
		byte MAX_ERRORS = 5;
		uint sendsize = 0;

		int gWaitTime = 250; //每一帧数据等待响应时间
		#endregion

		public enum TransferData
		{
			NormalTransfer,
			TransferInterupted,
			TransferAgain,
		}
		TransferData gTransData;

		BLE_APPLY_Class gBleApply;

		public Ymodem(BLE_APPLY_Class bleApply)
		{
			gBleApply = bleApply;
		}

		/// <summary>
		/// 将整数转换成字符
		/// </summary>
		/// <param name="str">字符</param>
		/// <param name="intnum">整数</param>
		private void Int2Str(ref byte[] str, uint intnum)
		{
			uint i, Div = 1000000000, j = 0, Status = 0;

			for (i = 0; i < 10; i++)
			{
				str[j++] = Convert.ToByte((intnum / Div) + 48);

				intnum = (uint)(intnum % Div);
				Div /= 10;
				if ((str[j - 1] == '0') & (Status == 0))
				{
					j = 0;
				}
				else
				{
					Status++;
				}
			}
		}

		/// <summary>
		/// 准备第一包要发送的数据 
		/// </summary>
		/// <param name="data">数据</param>
		/// <param name="fileName">文件名</param>
		/// <param name="length">文件大小</param>
		private void Ymodem_PrepareIntialPacket(ref byte[] data, byte[] fileName, uint length)
		{
			ushort i, j;
			byte[] file_ptr = new byte[10];

			/* 第一包数据的前三个字符  */
			data[0] = SOH; /* soh表示数据包是128字节 */
			data[1] = 0x00;
			data[2] = 0xff;

			/* 文件名 */
			//for (i = 0; (fileName[i] != '\0') && (i < FILE_NAME_LENGTH/2 + 3); i++)
			for (i = 0; (fileName[i] != '\0') && (i < fileName.Length); i++)
			{
				data[i + PACKET_HEADER] = fileName[i];
			}

			data[i + PACKET_HEADER] = 0x00;

			/* 文件大小转换成字符 */
			Int2Str(ref file_ptr, length);
			//byte[] file_ptr = Encoding.UTF8.GetBytes(length.ToString());

			for (j = 0, i += (ushort)(PACKET_HEADER + 1); file_ptr[j] != '\0';)
			{
				data[i++] = file_ptr[j++];
			}

			/* 其余补0 */
			//for (j = i; j < PACKET_SIZE + PACKET_HEADER; j++)
			//{
			//	data[j] = 0;
			//}
		}

		/// <summary>
		/// 准备发送数据包
		/// </summary>
		/// <param name="SourceBuf">要发送的原数据</param>
		/// <param name="data">最终要发送的数据包，已经包含的头文件和原数据</param>
		/// <param name="pktNo">数据包序号</param>
		/// <param name="sizeBlk">要发送数据数</param>
		void Ymodem_PreparePacket(byte[] SourceBuf, ref byte[] data, byte pktNo, uint sizeBlk)
		{
			int i;
			uint size, packetSize;
			string strSizeInfo;
			byte[] file_ptr = new byte[] { };

			/* 设置好要发送数据包的前三个字符data[0]，data[1]，data[2] */
			/* 根据sizeBlk的大小设置数据区数据个数是取1024字节还是取128字节*/
			packetSize = PACKET_SIZE;	//sizeBlk >= PACKET_1K_SIZE ? PACKET_1K_SIZE : PACKET_SIZE;

			/* 数据大小进一步确定 */
			size = sizeBlk < packetSize ? sizeBlk : packetSize;

			/* 首字节：确定是1024字节还是用128字节 */
			if (packetSize == PACKET_1K_SIZE)
			{
				data[0] = STX;
			}
			else
			{
				data[0] = SOH;
			}

			/* 第2个字节：数据序号 */
			data[1] = pktNo;
			/* 第3个字节：数据序号取反 */
			data[2] = Convert.ToByte((~pktNo) & 0xFF);
			file_ptr = SourceBuf;

			/* 填充要发送的原始数据 */
			for (i = 0; i < size ; i++)
			{
				data[i + PACKET_HEADER] = file_ptr[i];
			}

			/* 不足的补 EOF (0x1A) 或 0x00 */
			if (size <= packetSize)
			{
				for (i = (int)size + PACKET_HEADER; i < packetSize + PACKET_HEADER; i++)
				{
					data[i] = 0x1A; /* EOF (0x1A) or 0x00 */
				}
			}

			sendsize += size;
			strSizeInfo = string.Format("SendSize = {0:d}\r\n", sendsize);
			Console.Write(strSizeInfo);
		}

		/// <summary>
		/// 上次计算的CRC结果 crcIn 再加上一个字节数据计算CRC
		/// </summary>
		/// <param name="crcIn">上一次CRC计算结果</param>
		/// <param name="bt">新添加字节</param>
		/// <returns></returns>
		ushort UpdateCRC16(ushort crcIn, byte bt)
		{
			uint crc = crcIn;
			uint ind = (uint)bt | 0x100;

			do
			{
				crc <<= 1;
				ind <<= 1;
				if ((ind & 0x100) > 0)
					++crc;
				if ((crc & 0x10000) > 0)
					crc ^= 0x1021;
			} while ((ind & 0x10000) == 0);

			return (ushort)(crc & 0xffff);
		}

		/// <summary>
		/// 计算一串数据的CRC
		/// </summary>
		/// <param name="data">数据</param>
		/// <param name="size">数据长度</param>
		/// <returns>CRC计算结果</returns>
		ushort Cal_CRC16(byte[] data, uint size)
		{
			uint crc = 0;
			int j = 0;
			int k = data.Length;

			while (j < k)
				crc = UpdateCRC16((ushort)crc, data[j++]);

			crc = UpdateCRC16((ushort)crc, 0);
			crc = UpdateCRC16((ushort)crc, 0);

			return (ushort)(crc & 0xffff);
		}

		/// <summary>
		/// 计算一串数据总和
		/// </summary>
		/// <param name="data">数据</param>
		/// <param name="size">数据长度</param>
		/// <returns>计算结果的后8位</returns>
		byte CalChecksum(byte[] data, uint size)
		{
			uint sum = 0;
			int k = 0;
			int dataEnd = data.Length;

			while (k < dataEnd)
				sum += data[k++];

			return Convert.ToByte(sum & 0xff);
		}

		/// <summary>
		/// 拷贝数据至新缓存(除去第一次发送报文的前三个字节)
		/// </summary>
		/// <param name="SrcArr">源数据</param>
		/// <param name="DstArr">目标数据</param>
		void BytesCopy(byte[] SrcArr, ref byte[] DstArr)
		{
			Buffer.BlockCopy(SrcArr, 3, DstArr, 0, DstArr.Length);
		}

		/// <summary>
		/// 拷贝数据至新缓存
		/// </summary>
		/// <param name="SrcArr">源数据</param>
		/// <param name="nSrcOffset">源数据偏移量</param>
		/// <param name="DstArr">目标数据</param>
		/// <param name="nDstOffset">目标数据偏移量</param>
		void BytesCopy(byte[] SrcArr, int nSrcOffset, ref byte[] DstArr, int nDstLen = 0)
		{
			Buffer.BlockCopy(SrcArr, nSrcOffset, DstArr, 0, nDstLen);
		}

		/// <summary>
		/// 发送文件
		/// </summary>
		/// <param name="buf">文件数据</param>
		/// <param name="sendFileName">文件名</param>
		/// <param name="sizeFile">文件大小</param>
		/// <returns>文件发送结果</returns>
		byte Ymodem_Transmit(byte[] buf, byte[] sendFileName, uint sizeFile)
		{
			byte[] packet_data = new byte[PACKET_SIZE + PACKET_OVERHEAD];        
			byte[] packet_head_data = new byte[PACKET_SIZE + PACKET_HEADER];
			byte[] packet_head_data2 = new byte[PACKET_SIZE];
			byte[] packet_head_data_end = new byte[PACKET_SIZE + PACKET_HEADER];
			byte[] packet_dataCRC = new byte[PACKET_SIZE + PACKET_TRAILER];      
			byte[] filename = new byte[PACKET_SIZE];
			byte[] buf_ptr = new byte[] { };
			byte[] crc_arr = new byte[2];

			byte tempCheckSum;
			ushort tempCRC;
			byte blkNumber;
			byte[] receivedC = new byte[] { };
			byte CRC16_F = 0;
			byte i;
			uint errors, ackReceived, size, pktSize, buf_pos;
			string strPackData = string.Empty;
			string strRecvData = string.Empty;

			size = 0;
			errors = 0;
			buf_pos = 0;
			ackReceived = 0;

#if SendHeader
			#region 发送Ymoden协议头信息(SOH 0x00 0xFF FileName FileLen CRCL CRCH)

			//正常数据传输(数据头信息)
			//if (gTransData == TransferData.NormalTransfer)
			{
                for (i = 0; i < sendFileName.Length; i++)
				{
					filename[i] = sendFileName[i];
				}

				CRC16_F = 1;

				/* 初始化要发送的第一个数据包 */
				Ymodem_PrepareIntialPacket(ref packet_head_data, filename, sizeFile);

				do
				{						
TryAgain0:		/* 等待CRC响应到来 */
					receivedC = gBleApply.Read();
					if (receivedC[0] != CRC16)
					{/*没有收到正确应答，继续等待*/
						errors++;
						if (errors > 0x6A)
							break;
						Thread.Sleep(gWaitTime/2);
						goto TryAgain0;
					}
					else
						errors = 0;

					/* 根据CRC16_F发送CRC或者求和进行校验 */
					BytesCopy(packet_head_data, ref packet_head_data2);
					if (CRC16_F >= 1)
					{
						tempCRC = Cal_CRC16(packet_head_data2, PACKET_SIZE);

						//modified: combine all of header bytes into 1 array(that include SOH  |  序号 - 0x00 |  序号取反 - 0xff | 128字节数据，含文件名和文件大小字符串|CRC0 CRC1|)
						crc_arr[0] = (byte)((tempCRC >> 8) & 0xFF);
						crc_arr[1] = (byte)(tempCRC & 0xFF);
						packet_dataCRC = gBleApply.Combine(packet_head_data, crc_arr);
						gBleApply.Write(packet_dataCRC);
					}
					else
					{
						tempCheckSum = CalChecksum(packet_dataCRC, PACKET_SIZE);
						gBleApply.WriteByte(tempCheckSum);
					}

TryAgain1:
					/* 等待 Ack 和字符 'C' */
					Thread.Sleep(gWaitTime);

					//蓝牙已断开
					if (!gBleApply.gBLEConStatus)
					{
						return 0xFF;
					}
					receivedC = gBleApply.Read();
					if (receivedC.Length == 1)
					{
						if (receivedC[0] == CRC16)
						{/*没有收到正确应答，继续等待*/
							errors++;
							if (errors > 0x0A)
								break;

							goto TryAgain1;
						}
					}
                    else if (receivedC.Length == 2)
                    {
						if ((receivedC[0] == ACK) && (receivedC[1] == CRC16))
						{
							/* 接收到应答 */
							ackReceived = 1;
							break;
						}
						else/* 没有等到 */
						{
							errors++;
						}
					}

					/* 发送数据包后接收到应答或者没有等到就推出 */
				} while (ackReceived != 1 && (errors < 0x0A));

				/* 超过最大错误次数就退出 */
				if (errors >= 0x0A)
				{
					Console.WriteLine("发送报文头请求后，控制板卡无响应");
					Console.Read();
					return (byte)errors;
				}
			}
			#endregion

#endif
			buf_ptr = buf;
			size = sizeFile;
			blkNumber = 0x01;

			/* 下面使用的是发送128/1024字节数据包 */
			/* Resend packet if NAK  for a count of 10 else end of communication */
			while (size > 0)
			{
				/* 准备下一包数据 */
				byte[] buf_ptr_in_process = new byte[PACKET_SIZE + PACKET_HEADER]; 
				if((sizeFile - buf_pos) >= buf_ptr_in_process.Length)
					BytesCopy(buf_ptr, (int)buf_pos, ref buf_ptr_in_process, buf_ptr_in_process.Length);
				else
					BytesCopy(buf_ptr, (int)buf_pos, ref buf_ptr_in_process, (int)size);

				byte[] packet_block_data = new byte[PACKET_SIZE + PACKET_HEADER];
				Ymodem_PreparePacket(buf_ptr_in_process, ref packet_block_data, blkNumber, size);

				ackReceived = 0;
				receivedC[0] = 0;
				errors = 0;

				do
				{
                    /* 发送下一包数据 */
                    //if (size >= PACKET_1K_SIZE)
                    //{
                    //    pktSize = PACKET_1K_SIZE;
                    //}
                    //else
                    {
						pktSize = PACKET_SIZE;
					}

					//向要发送的数据包尾部添加CRC校验

					/* 根据CRC16_F发送CRC或者求和进行校验 */
					BytesCopy(packet_block_data, ref packet_head_data2);
					tempCRC = Cal_CRC16(packet_head_data2, PACKET_SIZE);

					crc_arr[0] = (byte)((tempCRC >> 8) & 0xFF); 
					crc_arr[1] = (byte)(tempCRC & 0xFF);
					packet_data = gBleApply.Combine(packet_block_data, crc_arr);
					//_

					gBleApply.Write(packet_data);
TryAgain:
					/* 等待Ack信号 */
					Thread.Sleep(gWaitTime);

					//蓝牙已断开
					if (!gBleApply.gBLEConStatus)
					{
						return 0xFF;
					}

					receivedC = gBleApply.Read();
					if (receivedC[0] == ACK || receivedC[0] == CRC16)
					{
						ackReceived = 1;
						/* 修改buf_ptr位置以及size大小，准备发送下一包数据 */
						if (size > pktSize)
						{
							buf_pos += pktSize;
							size -= pktSize;
							if (blkNumber == 0xFF)
							{
								blkNumber = 0x0; //数据包序号只有1字节，因此计算范围是0~255；对于数据包大于255的，序号归零重复计算。
							}
							else
							{
								blkNumber++;
							}
						}
						else
						{
							buf_pos += pktSize;
							size = 0;
						}
					}
					else
					{
						errors++;
						if (errors > 0x0A)
							break;

						goto TryAgain;
					}

				} while (ackReceived != 1 && (errors < 0x0A));

				/* 超过10次没有收到应答就退出 */
				if (errors >= 0x0A)
				{
					Console.WriteLine("收到下位机负响应次数过多，数据传输失败。");
					Console.Read();
					return (byte)errors;
				}
			}

			/* 发送EOT信号 */
			ackReceived = 0;
			receivedC[0] = 0x00;
			errors = 0;

			do
			{
				gBleApply.WriteByte(EOT);
								
				/* 等待Ack应答 */
				Thread.Sleep(gWaitTime);

				//蓝牙已断开
				if (!gBleApply.gBLEConStatus)
				{
					return 0xFF;
				}

				receivedC = gBleApply.Read();
				if (receivedC[0] == ACK)
					ackReceived = 1;
				else
					errors++;			
			} while (ackReceived != 1 && (errors < 0x0A));

			if (errors >= 0x0A)
			{
				return (byte)errors;
			}

			Console.Write("发送结束信号\r\n");

#if LastPackage
			/* 初始化最后一包要发送的数据 */
			ackReceived = 0;
			receivedC[0] = 0x00;
			errors = 0;

			packet_head_data_end[0] = SOH;
			packet_head_data_end[1] = 0;
			packet_head_data_end[2] = 0xFF;

			/* 数据包的数据部分全部初始化为0 */
			for (i = PACKET_HEADER; i < PACKET_SIZE ; i++)
			{
				packet_head_data_end[i] = 0x00;
			}
			/*crc code添加至数据尾部*/
			BytesCopy(packet_head_data_end, ref packet_head_data2);
			tempCRC = Cal_CRC16(packet_head_data2, PACKET_SIZE);
			crc_arr[0] = (byte)((tempCRC >> 8) & 0xFF);
			crc_arr[1] = (byte)(tempCRC & 0xFF);
			packet_data = gBleApply.Combine(packet_head_data_end, crc_arr);

			do 
			{
				/* 发送数据包 */
				gBleApply.Write(packet_data);

				/* 等待 Ack */
				Thread.Sleep(gWaitTime);

				//蓝牙已断开
				if (!gBleApply.gBLEConStatus)
				{
					return 0xFF;
				}

				receivedC = gBleApply.Read();
				if (receivedC[0] == ACK)
				{
					/* 数据包发送成功 */
					ackReceived = 1;
				}
				else
				{
					errors++;
				}
			}while (ackReceived!=1 && (errors < 0x0A));

			/* 超过10次没有收到应答就退出 */
			if (errors >=  0x0A)
			{
				Console.Write("文件发送失败!\r\n");
				return (byte)errors;
			}
			else
            {
				Console.Write("处理完毕,文件发送成功!\r\n");
			}
#endif

			return 0; /* 文件发送成功 */
		}

		/// <summary>
		/// 读取bin文件数据，调用蓝牙发送函数，按照ymoden协议向控制板发送数据
		/// </summary>
		/// <param name="strBinFilename">bin文件名</param>
		/// <returns>发送成功？</returns>
		public int xymodem_send(string strBinFilename)
		{
			int nRet = -1;
			long lFileLength = 0;
			FileStream fs = null;

			//发送请求下位机工作模式转换进Ymoden协议，进行数据传输
			if (SendModeConvertReq())
			{
				try				
				{
					if(strBinFilename==string.Empty)
                    {
						Console.Write("请输入要传输的全路径文件名\t\n");
						return nRet;
                    }

					FileInfo fi = new FileInfo(strBinFilename);
					if (!fi.Exists)
					{
						Console.Write("Inputed .bin file not exist or file path not correct,please verify.");
						return nRet;
					}

					fs = new FileStream(strBinFilename, FileMode.Open, FileAccess.Read);
					lFileLength = fs.Length;

					byte[] fBuffer = new byte[lFileLength];
					fs.Read(fBuffer, 0, (int)lFileLength);

					byte[] fileName = Encoding.UTF8.GetBytes(fi.Name);

					if (0 == Ymodem_Transmit(fBuffer, fileName, (uint)lFileLength))
					{
						nRet = 0;
						Console.Write("数据传输完成，按任意键退出\t\n");
					}
					else
                    {
						Console.WriteLine("数据传输失败，按任意键退出\t\n");
						Console.Read();
                    }
				}
				finally
				{
					if(fs!=null)
						fs.Close();
					Console.Read();
				}
			}
			else
            {
				Console.Write("控制板卡响应超时，请检查！");
				Console.Read();
            }
				
			return nRet;
		}

		/// <summary>
		/// 发送模式转换请求（转换进Ymoden协议，进行数据传输)
		/// </summary>
		private bool SendModeConvertReq()
        {
			bool bRight = false;
			byte[] receivedC = new byte[] { };
			byte[] enterYmoden_req = new byte[] { 0x1B, 0x00, 0x05, 0x00, 0x53, 0x54, 0x01, 0x5F };

			//将 GattCharastic设置为 WriteWithoutReponse | Write
			gBleApply.BtnOpt(6);
			Thread.Sleep(500);

			gBleApply.WriteKey(enterYmoden_req);
			Thread.Sleep(500);
			receivedC = gBleApply.Read();

			if (receivedC[0] == 0x57)
            {
				gBleApply.WriteByte(0x57);
				bRight = true;
				gTransData = TransferData.NormalTransfer;
			}
			else if(receivedC[0] == 0x43) //测试用临时代码
            {
				bRight = true;
				gTransData = TransferData.TransferInterupted;
			}
			
			return bRight;
		}







	}
}
