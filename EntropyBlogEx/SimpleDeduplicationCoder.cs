using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BinEntropy;

namespace EntropyBlogEx
{
    class SimpleDeduplicationCoder
    {
        private struct Model
        {
            enum LiteralFlagProbs
            {
                HIGH = 0,
                LOW = 1,
                MODERATE_LOW = 2
            };
            public SimpleStaticModel[] literalFlag;

            public SimpleStaticModel[] literal;

            public BinShiftModel indexSize;

            public BinShiftModel index0;
            public BinShiftModel[] index1;

            public BinShiftModel reuseRepeat;

            public BinShiftModel repeatSize;

            public BinShiftModel[] repeat0;
            public BinShiftModel[] repeat1;

            public void Init()
            {
                literalFlag = new SimpleStaticModel[4];

                // rules for coding: 
                // - first is always a 1, use high probability
                // - after 1 en/de-coded, switch to low-probability
                // - after 0 decoded switch to moderate-low probability
                float[] probs = { 0.999f,   // high-prob
                                  0.1f,     // low-prob
                                  0.4f };   // moderate-low prob
                for (int i = 0; i < literalFlag.Length; ++i)
                {
                    literalFlag[i] = new SimpleStaticModel();
                    literalFlag[i].SetProb(probs[i]);
                }

                // codes literal bits with static 1-bit
                literal = new SimpleStaticModel[4];
                for (int i = 0; i < literal.Length; ++i)
                {
                    literal[i] = new SimpleStaticModel();
                    literal[i].SetProb(0.50f);
                }

                // the adaptive model inertias can be adjusted
                // to minimize data size
                indexSize = new BinShiftModel
                {
                    Inertia = 3
                };
                indexSize.SetProb(0.1f); // index_size is likely to start small

                index0 = new BinShiftModel { Inertia = 8 };
                index1 = new BinShiftModel[2];
                for (int i = 0;i < index1.Length; ++i)
                {
                    index1[i] = new BinShiftModel { Inertia = 8 };
                };


                reuseRepeat = new BinShiftModel
                {
                    Inertia = 6
                };

                repeatSize = new BinShiftModel
                {
                    Inertia = 5
                };

                repeat0 = new BinShiftModel[2];
                repeat1 = new BinShiftModel[4];

            }
        }
    }
}
