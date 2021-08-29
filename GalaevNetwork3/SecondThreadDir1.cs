using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using GalaevNetwork3.BitArrayRoutine;

namespace GalaevNetwork3
{
    public class SecondThreadDir1
    {
        private Semaphore _sendSemaphore;
        private Semaphore _receiveSemaphore;
        private BitArray _receivedMessage;
        private BitArray _sendMessage;
        private PostToFirstWT _post;

        private Frame[] _receiveBuffer = new Frame[C.ReceiverBufferSize];
        private int _receiveBufferCount = 0;
        private List<BitArray> _receivedDataBits = new();

        private Random _random = new(DateTime.Now.Millisecond);

        public SecondThreadDir1(ref Semaphore sendSemaphore, ref Semaphore receiveSemaphore)
        {
            _sendSemaphore = sendSemaphore;
            _receiveSemaphore = receiveSemaphore;
        }

        public void SecondThreadMain(Object obj)
        {
            _post = (PostToFirstWT)obj;
            ConsoleHelper.WriteToConsole("2 поток (1->2)", "Начинаю работу.");

            bool connected = false;

            while (!connected)
            {
                WaitForDataWithTimeout();
                ConsoleHelper.WriteToConsole("2 поток (1->2)", "Получен запрос на подключение!");

                var connectFrame = Frame.Parse(_receivedMessage);
                if (connectFrame.Control.ToByteArray()[0] != (byte)RequestType.Connect)
                {
                    ConsoleHelper.WriteToConsole("2 поток (1->2)", "Подключение отклонено!");

                    _sendMessage = BuildREJFrame().Build();
                    _post(_sendMessage);
                    _sendSemaphore.Release();
                }
                else
                {
                    if (_random.Next(0, 1000) < 100)
                    {
                        // 90% chance
                        _sendMessage = BuildREJFrame().Build();
                        _post(_sendMessage);
                        _sendSemaphore.Release();
                    }
                    else
                    {
                        ConsoleHelper.WriteToConsole("2 поток (1->2)", "Подключение одобрено!");
                        _sendMessage = BuildRRFrame().Build();
                        _post(_sendMessage);
                        _sendSemaphore.Release();

                        connected = true;
                    }
                }
            }

            bool isInBlock = false;

            while (connected)
            {
                WaitForDataWithTimeout();

                if (_random.Next(0, 1000) < 100)
                {
                    var randomBitIndex = _random.Next(0, _receivedMessage.Count);

                    _receivedMessage[randomBitIndex] = !_receivedMessage[randomBitIndex];
                }

                Frame frame;

                try
                {
                    frame = Frame.Parse(_receivedMessage);
                }
                catch
                {
                    ConsoleHelper.WriteToConsole("2 поток (1->2)", "Не удалось распознать кадр!");
                    _sendMessage = BuildREJFrame().Build();
                    _post(_sendMessage);
                    _sendSemaphore.Release();
                    continue;
                }
                
                var receivedChecksum = frame.Checksum;
                var actualChecksum = frame.BuildChecksum();
                if (!receivedChecksum.IsSameNoCopy(actualChecksum, 0, 0, C.ChecksumSize))
                {
                    ConsoleHelper.WriteToConsole("2 поток (1->2)", "Контрольная сумма кадра не совпала!");

                    _sendMessage = BuildREJFrame().Build();
                    _post(_sendMessage);
                    _sendSemaphore.Release();
                    continue;
                }

                if (frame.Control.ToByteArray()[0] == (byte)RequestType.Start)
                {
                    ConsoleHelper.WriteToConsole("2 поток (1->2)", "Начало блока данных!");
                    isInBlock = true;
                    _sendMessage = BuildRRFrame().Build();
                    _post(_sendMessage);
                    _sendSemaphore.Release();
                }
                else if (frame.Control.ToByteArray()[0] == (byte)RequestType.End)
                {
                    ConsoleHelper.WriteToConsole("2 поток (1->2)", "Конец блока данных!");

                    isInBlock = false;

                    DrainBuffer();

                    var receivedBytes = GetReceivedBytes();
                    
                    File.WriteAllBytes("received.txt", receivedBytes);

                    ConsoleHelper.WriteToConsole("2 поток (1->2)", $"Получены данные");

                    var process = Process.Start("notepad.exe", "received.txt");

                    process.WaitForExit();

                    _receivedDataBits.Clear();

                    _sendMessage = BuildRRFrame().Build();
                    _post(_sendMessage);
                    _sendSemaphore.Release();
                }
                else if (frame.Control.ToByteArray()[0] == (byte)RequestType.Disconnect)
                {
                    if (isInBlock)
                    {
                        ConsoleHelper.WriteToConsole("2 поток (1->2)", "Получен запрос на отключение во время получения данных!");

                        DrainBuffer();

                        var receivedBytes = GetReceivedBytes();

                        ConsoleHelper.WriteToConsole("2 поток (1->2)", $"Частично полученные данные: \"{receivedBytes}\"");
                        _receivedDataBits.Clear();
                    }

                    // Respond to disconnection
                    _sendMessage = BuildRRFrame().Build();
                    _post(_sendMessage);
                    _sendSemaphore.Release();
                    connected = false;
                }
                else if (frame.Control.ToByteArray()[0] == (byte)RequestType.Data)
                {
                    ConsoleHelper.WriteToConsole("2 поток (1->2)", "Получен кадр данных!");
                    _receiveBuffer[_receiveBufferCount++] = frame;
                    if (_receiveBufferCount == C.ReceiverBufferSize)
                    {
                        DrainBuffer();
                    }

                    _sendMessage = BuildRRFrame().Build();
                    _post(_sendMessage);
                    _sendSemaphore.Release();
                }
            }

            ConsoleHelper.WriteToConsole("2 поток (1->2)", "Заканчиваю работу");
        }

        private Frame BuildRRFrame()
        {
            var frame = new Frame(new BitArray(C.ControlSize), new BitArray(0));

            var bitArrayWriter = new BitArrayWriter(frame.Control);
            bitArrayWriter.Write(new BitArray(new[] {(byte)ResponseStatus.RR, (byte)0}));

            return frame;
        }

        private Frame BuildRNRFrame()
        {
            var frame = new Frame(new BitArray(C.ControlSize), new BitArray(0));

            var bitArrayWriter = new BitArrayWriter(frame.Control);
            bitArrayWriter.Write(new BitArray(new[] {(byte)ResponseStatus.RNR, (byte)0}));

            return frame;
        }

        private Frame BuildREJFrame()
        {
            var frame = new Frame(new BitArray(C.ControlSize), new BitArray(0));

            var bitArrayWriter = new BitArrayWriter(frame.Control);
            bitArrayWriter.Write(new BitArray(new[] {(byte)ResponseStatus.REJ, (byte)0}));

            return frame;
        }

        private byte[] GetReceivedBytes()
        {
            var totalReceivedBitsCount = _receivedDataBits.Sum(b => b.Count);
            var totalReceivedBits = new BitArray(totalReceivedBitsCount);
            var totalReceivedBitsWriter = new BitArrayWriter(totalReceivedBits);
            for (int i = 0; i < _receivedDataBits.Count; i++)
            {
                totalReceivedBitsWriter.Write(_receivedDataBits[i]);
            }

            var receivedBytes = totalReceivedBits.ToByteArray();

            return receivedBytes;
        }

        private void DrainBuffer()
        {
            for (int i = 0; i < _receiveBufferCount; i++)
            {
                _receivedDataBits.Add(_receiveBuffer[i].Data);
            }

            _receiveBufferCount = 0;
        }

        private void WaitForDataWithTimeout()
        {
            int tries = 0;
            while (!_receiveSemaphore.WaitOne(500) && tries < 3)
            {
                // Timeout
                ConsoleHelper.WriteToConsole("2 поток (1->2)", $"Таймаут получения {++tries} раз");
            }
        }

        public void ReceiveData(BitArray array)
        {
            _receivedMessage = array;
        }
    }
}