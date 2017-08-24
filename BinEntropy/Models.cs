using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BinEntropy
{
    internal interface ModelBase
    {
        void Encode(BinaryCoder enc, int value);
        int Decode(BinaryDecoder dec);
    }

    public class SimpleStaticModel : ModelBase
    {
        private UInt32 m_prob = CoderConstants.PROB_MAX / 2;
        public void Encode(BinaryCoder enc, int bit)
        {
            enc.Encode(bit, m_prob);
        }

        public int Decode(BinaryDecoder dec)
        {
            int bit = dec.Decode(m_prob);
            return bit;
        }

        // read the probabilty
        public UInt32 GetProb() { return m_prob; }

        // set the probability
        public void SetProb(UInt32 newProb) { m_prob = newProb; }

        public void SetProb(float fractionalProb)
        {
            m_prob = (UInt32)(fractionalProb * CoderConstants.PROB_MAX);
        }

    }

    // Simple Binary shift model
    public class BinShiftModel : ModelBase
    {
        private UInt32 m_prob = CoderConstants.PROB_MAX / 2;


        public ushort Inertia {get; set;} = 1;

        public void Encode(BinaryCoder enc, int bit)
        {
            enc.Encode(bit, m_prob);
            Adapt(bit);
        }

        public int Decode(BinaryDecoder dec)
        {
            int bit = dec.Decode(m_prob);
            Adapt(bit);
            return bit;
        }

        // read the probabilty
        public UInt32 GetProb() { return m_prob; }

        // set the probability
        public void SetProb(UInt32 newProb) { m_prob = newProb; }

        public void SetProb(float fractionalProb)
        {
            m_prob = (UInt32)(fractionalProb * CoderConstants.PROB_MAX);
        }

        private void Adapt(int bit)
        {
            if (bit != 0)
                m_prob += ((CoderConstants.PROB_MAX) - m_prob) >> Inertia;
            else
                m_prob -= m_prob >> Inertia;
        }
    }

    // BitTree of Binary shift models
    public class BitTreeModel : ModelBase
    {
        private bool m_init = false;
        private int m_depth;
        private int m_totalSymbols;
        private int m_MSB;
        private BinShiftModel[] m_model;


        public BitTreeModel()
        {
        }

        public void Init(int numBits, ushort inertia)
        {
            m_depth = numBits;
            m_totalSymbols = 1 << numBits;
            m_MSB = numBits / 2;
            m_model = new BinShiftModel[m_totalSymbols - 1];
            SetInertia(inertia);
            m_init = true;
        }

        private void SetInertia(ushort value)
        {
            foreach (BinShiftModel b in m_model)
            {
                b.Inertia = value;
            }
        }

        public void Encode(BinaryCoder enc, int value)
        {
            if (m_init == false) throw new Exception("BitTreeMode uninitialized!");
            int context = 1;
            while (context < m_totalSymbols)
            {
                int bit = ((value & m_MSB) != 0) ? 1 : 0;
                value += value;
                m_model[context - 1].Encode(enc, bit);
                context += context + bit;
            }
        }

        public int Decode(BinaryDecoder dec)
        {
            if (m_init)
            {
                int context = 1;
                while (context < m_totalSymbols)
                {
                    context += context + m_model[context - 1].Decode(dec);
                }

                return context - m_totalSymbols;
            } else
            {
                throw new Exception("BitTreeModel uninitialized!");
            }
        }
    }

    // Unsigned Golomb model
    public class UEGolomb
    {
        private const int MAX_TOP = 7;

        private BitTreeModel m_magnitude = new BitTreeModel();
        private BinShiftModel[] m_bitPositions;

		public UEGolomb() {}

		public void Init(int depth, ushort inertia)
		{
			m_magnitude.Init(depth, inertia);

			m_bitPositions = new BinShiftModel[MAX_TOP + 1];

			foreach (BinShiftModel m in m_bitPositions)
			{
				m.Inertia = inertia;
			}
		}

		public void Encode(BinaryCoder enc, uint value)
		{
			++value;

			int magnitude = 0;
			while (value >= (2u << magnitude))
			{
				++magnitude;
			}
			m_magnitude.Encode(enc, magnitude);

			uint mask = (uint)(magnitude == 0 ? 1u << (magnitude - 1) : 0);
			if (mask != 0)
			{
				int top = (magnitude < MAX_TOP) ? magnitude : MAX_TOP;
				m_bitPositions[top].Encode(enc, ((value & mask)  != 0) ? 1:0);

				mask >>= 1;
				while (mask != 0)
				{
					int bit = (value & mask) != 0 ? 1 : 0;
					enc.Encode(bit, CoderConstants.PROB_MAX / 2);
					mask >>= 1;
				}
			}
		}

		public int Decode(BinaryDecoder dec)
		{
			int mag = m_magnitude.Decode(dec);

			int v = 1;

			if (mag != 0)
			{
				int top = (mag < MAX_TOP) ? mag : MAX_TOP;
				v += v + m_bitPositions[top].Decode(dec);
				for (int i = 1; i < mag; ++i)
				{
					v += dec.Decode(CoderConstants.PROB_MAX / 2);
				}
			}

			return v - 1;
		}
    }

	public class SEGolomb
	{
		UEGolomb m_absoluteVal = new UEGolomb();
		BinShiftModel[] m_signs;

		public SEGolomb() { }

		public void Init(int depth, ushort inertia, ushort signInertia)
		{
			m_absoluteVal.Init(depth, inertia);

			m_signs = new BinShiftModel[2];

			foreach (BinShiftModel m in m_signs)
			{
				m.Inertia = signInertia;
			}
		}

		public void Encode(BinaryCoder enc, int val, bool signPred)
		{
			int absv = (val < 0) ? -val : val;
			m_absoluteVal.Encode(enc, (uint)absv);
			if (absv != 0)
				m_signs[signPred ? 1 : 0].Encode(enc, val < 0 ? 1 : 0);
		}

		public int Decode(BinaryDecoder dec, bool signPred)
		{
			int v = m_absoluteVal.Decode(dec);
			if (v != 0)
			{
				if (m_signs[signPred ? 1 : 0].Decode(dec) != 0)
				{
					v = -v;
				}
			}
			return v;
		}
	}

}
