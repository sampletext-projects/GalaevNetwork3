using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using GalaevNetwork3.BitArrayRoutine;

namespace GalaevNetwork3
{
    public class SecondThreadDir2
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

        public SecondThreadDir2(ref Semaphore sendSemaphore, ref Semaphore receiveSemaphore)
        {
            _sendSemaphore = sendSemaphore;
            _receiveSemaphore = receiveSemaphore;
        }

        public void SecondThreadMain(Object obj)
        {
            _post = (PostToFirstWT)obj;
            ConsoleHelper.WriteToConsole("2 поток (2->1)", "Начинаю работу.");

            bool connected = false;

            while (!connected)
            {
                WaitForDataWithTimeout();
                ConsoleHelper.WriteToConsole("2 поток (2->1)", "Получен запрос на подключение!");

                var connectFrame = Frame.Parse(_receivedMessage);
                if (connectFrame.Control.ToByteArray()[0] != (byte)RequestType.Connect)
                {
                    ConsoleHelper.WriteToConsole("2 поток (2->1)", "Подключение отклонено!");

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
                        ConsoleHelper.WriteToConsole("2 поток (2->1)", "Подключение одобрено!");
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
                    ConsoleHelper.WriteToConsole("2 поток (2->1)", "Не удалось распознать кадр!");
                    _sendMessage = BuildREJFrame().Build();
                    _post(_sendMessage);
                    _sendSemaphore.Release();
                    continue;
                }
                
                var receivedChecksum = frame.Checksum;
                var actualChecksum = frame.BuildChecksum();
                if (!receivedChecksum.IsSameNoCopy(actualChecksum, 0, 0, C.ChecksumSize))
                {
                    ConsoleHelper.WriteToConsole("2 поток (2->1)", "Контрольная сумма кадра не совпала!");

                    _sendMessage = BuildREJFrame().Build();
                    _post(_sendMessage);
                    _sendSemaphore.Release();
                    continue;
                }

                if (frame.Control.ToByteArray()[0] == (byte)RequestType.Start)
                {
                    ConsoleHelper.WriteToConsole("2 поток (2->1)", "Начало блока данных!");
                    isInBlock = true;
                    _sendMessage = BuildRRFrame().Build();
                    _post(_sendMessage);
                    _sendSemaphore.Release();
                }
                else if (frame.Control.ToByteArray()[0] == (byte)RequestType.End)
                {
                    ConsoleHelper.WriteToConsole("2 поток (2->1)", "Конец блока данных!");

                    isInBlock = false;

                    DrainBuffer();

                    var receivedString = GetReceivedString();

                    ConsoleHelper.WriteToConsole("2 поток (2->1)", $"Получены данные: \"{receivedString}\"");
                    _receivedDataBits.Clear();

                    _sendMessage = BuildRRFrame().Build();
                    _post(_sendMessage);
                    _sendSemaphore.Release();
                }
                else if (frame.Control.ToByteArray()[0] == (byte)RequestType.Disconnect)
                {
                    if (isInBlock)
                    {
                        ConsoleHelper.WriteToConsole("2 поток (2->1)", "Получен запрос на отключение во время получения данных!");

                        DrainBuffer();

                        var receivedString = GetReceivedString();

                        ConsoleHelper.WriteToConsole("2 поток (2->1)", $"Частично полученные данные: \"{receivedString}\"");
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
                    ConsoleHelper.WriteToConsole("2 поток (2->1)", "Получен кадр данных!");
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

            ConsoleHelper.WriteToConsole("2 поток (2->1)", "Заканчиваю работу");
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

        private string GetReceivedString()
        {
            var totalReceivedBitsCount = _receivedDataBits.Sum(b => b.Count);
            var totalReceivedBits = new BitArray(totalReceivedBitsCount);
            var totalReceivedBitsWriter = new BitArrayWriter(totalReceivedBits);
            for (int i = 0; i < _receivedDataBits.Count; i++)
            {
                totalReceivedBitsWriter.Write(_receivedDataBits[i]);
            }

            var receivedBytes = totalReceivedBits.ToByteArray();

            var receivedString = Encoding.UTF8.GetString(receivedBytes);
            return receivedString;
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
                ConsoleHelper.WriteToConsole("2 поток (2->1)", $"Таймаут получения {++tries} раз");
            }
        }

        public void ReceiveData(BitArray array)
        {
            _receivedMessage = array;
        }
    }
}