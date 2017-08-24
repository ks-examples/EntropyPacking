using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BinEntropy
{
    internal class CoderConstants
    {
        public const int PROB_BITS = 31;
        public const UInt32 PROB_MAX = 1u << PROB_BITS;
    }


    public class BinaryCoder
    {
        private UInt32 m_high = ~0u, m_low = 0;
        private Stream m_output;

        public BinaryCoder(Stream outputStream)
        {
            m_output = outputStream;
        }

        public void Encode(int bit, UInt32 probability)
        {
            UInt32 x = m_low + (UInt32)(((UInt64)(m_high - m_low) * probability) >> CoderConstants.PROB_BITS);

            if (bit != 0)
                m_high = x;
            else
                m_low = x + 1;


            // this is renormalization - shift out topmost bits that match
            while ((m_low ^ m_high) < 0x01000000)
            {
                m_output.WriteByte((byte)(m_low >> 24));
                m_low <<= 8;
                m_high = (m_high << 8) | 0x000000ff;
            }
        }

        public void Flush()
        {
            UInt32 roundUp = 0x00ffffffu;
            while (roundUp != 0) {
                if ((m_low | roundUp) != 0xffffffffu)
                {
                    UInt32 rounded = (m_low + roundUp) & ~roundUp;
                    if (rounded <= m_high)
                    {
                        m_low = rounded;
                        break;
                    }
                }

                roundUp >>= 8;
            }

            while (m_low != 0) {
                m_output.WriteByte((byte)(m_low >> 24));
                m_low <<= 8;
            }
            m_output.Flush();
        }
    }

    public class BinaryDecoder
    {
        private UInt32 m_high = ~0u, m_low = 0, m_code = 0;
        private Stream m_input;

        private byte GetByte()
        {
            if (m_input.CanRead)
            {
                int val = m_input.ReadByte();
                if (val != -1)
                    return (byte)val;
                else
                    return 0; // end of stream, just 0-fill
            }
            else
            {
                throw new Exception("Unexpectedly unable to read from decode stream");
            }
        }

        public BinaryDecoder(Stream source)
        {
            m_input = source;
            for (int i = 0; i < 4; ++i)
            {
                m_code = (m_code << 8) | GetByte();
            }
        }

        public int Decode(UInt32 probability)
        {
            int bit = 0;
//            UInt32 x = (UInt32)(m_low + ((UInt64)(m_high - m_low) * probability) >> CoderConstants.PROB_BITS);
            UInt32 x = m_low + (UInt32)(((UInt64)(m_high - m_low) * probability) >> CoderConstants.PROB_BITS);

            if (m_code <= x )
            {
                m_high = x;
                bit = 1;
            }
            else
            {
                m_low = x + 1;
                bit = 0;
            }

            // shift in the next byte - reverse renormalization
            while ((m_low ^ m_high) < 0x01000000)
            {
                m_code = (m_code << 8) | GetByte();
                m_low <<= 8;
                m_high = (m_high << 8) | 0x000000ff;
            }

            return bit;
        }

    }
}
