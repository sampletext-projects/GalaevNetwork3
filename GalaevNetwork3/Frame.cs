using System;
using System.Collections;
using GalaevNetwork3.BitArrayRoutine;

namespace GalaevNetwork3
{
    public class Frame
    {
        public static readonly BitArray Flag = new(new[] {false, true, true, true, true, true, true, false});

        // 1 байт - контрольные флаги, 2 байт - длина кадра (количество бит в Data)
        public BitArray Control { get; set; }

        public BitArray Data { get; set; }

        public BitArray Checksum { get; set; }

        public Frame(BitArray control, BitArray data)
        {
            Control = control;
            Data = data;
        }

        public BitArray Build()
        {
            WriteDataLengthToControl();

            Checksum = BuildChecksum();

            int frameSize =
                C.FlagSize +
                C.ControlSize +
                Data.Length +
                C.ChecksumSize +
                C.FlagSize;

            BitArray frameArray = new BitArray(frameSize);

            var writer = new BitArrayWriter(frameArray);

            writer.Write(Flag);
            writer.Write(Control);
            writer.Write(Data);
            writer.Write(Checksum);
            writer.Write(Flag);

            return frameArray;
        }

        private void WriteDataLengthToControl()
        {
            var controlWriter = new BitArrayWriter(Control, 8);

            controlWriter.Write(new BitArray(new[] {(byte)Data.Count}));
        }

        public BitArray BuildChecksum()
        {
            var checksumBitArray = new BitArray(C.ControlSize + Data.Count);
            var checksumBitArrayWriter = new BitArrayWriter(checksumBitArray);
            checksumBitArrayWriter.Write(Control);
            checksumBitArrayWriter.Write(Data);

            var checksumCRC = checksumBitArray.ToByteArray().CRC16();
            return new BitArray(BitConverter.GetBytes(checksumCRC));
        }

        public static Frame Parse(BitArray rawBits)
        {
            if (rawBits.FindFlag(0) == -1)
            {
                throw new ArgumentException($"{nameof(rawBits)} doesn't contain start Flag");
            }

            if (rawBits.FindFlag(C.FlagSize) == -1)
            {
                throw new ArgumentException($"{nameof(rawBits)} doesn't contain second Flag");
            }

            var rawBitsReader = new BitArrayReader(rawBits);

            rawBitsReader.Read(C.FlagSize);
            var controlBits = rawBitsReader.Read(C.ControlSize);
            var dataLengthInBits = controlBits.ToByteArray()[1];
            var dataBits = rawBitsReader.Read(dataLengthInBits);
            var checksumBits = rawBitsReader.Read(C.ChecksumSize);

            return new Frame(controlBits, dataBits) {Checksum = checksumBits};
        }

        public override string ToString()
        {
            return
                $"Frame {{\n  {nameof(Data)}: {Data.ToBinString()},\n  {nameof(Control)}: {Control.ToBinString()},\n  {nameof(Checksum)}: {Checksum.ToBinString()}\n}}";
        }
    }
}