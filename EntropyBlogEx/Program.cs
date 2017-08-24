using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BinEntropy;
using System.IO;

namespace EntropyBlogEx
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Binary Range Coder Example");
            UnitTest();
            SimpleExample();
        }


        static void SimpleExample()
        {
            // let's construct some data consisting of runs of 0's and 1's.
            byte[] simpleSampleData =
            {
                0x00,
                0x00,
                0x00,
                0x0f,

                0xff,
                0xf0,
                0x00,
                0x1f,

                0xff,
                0xff,
                0xff,
                0xff,

                0xf1,
                0xe0,
                0x0c,
                0xf0
            };


            // Naiive adaptive example
            {
                System.IO.MemoryStream output = new System.IO.MemoryStream();
                BinaryCoder coder = new BinaryCoder((Stream)output);
                BinShiftModel adaptiveModel = new BinShiftModel();

                // the lower the number, the faster it adapts
                adaptiveModel.Inertia = 1;

                foreach (byte b in simpleSampleData)
                {
                    for (int bitpos = 7; bitpos >= 0; --bitpos)
                    {
                        int bit = (int)(b >> bitpos) & 1;
                        // NB: Skipping try/catch here!
                        adaptiveModel.Encode(coder, bit);
                    }
                }
                coder.Flush();

                Console.WriteLine("Adaptive Output size is " + output.Length);
            }

            // Context-switching example
            {
                // encode
                System.IO.MemoryStream output2 = new System.IO.MemoryStream();
                BinaryCoder coder2 = new BinaryCoder((Stream)output2);
                SimpleStaticModel model0 = new SimpleStaticModel();
                SimpleStaticModel model1 = new SimpleStaticModel();

                model0.SetProb(10f / 128f);
                model1.SetProb(1f - (10f / 128f));

                bool use0Context = true; // 0 bits come first, use the correct model!

                foreach (byte b in simpleSampleData)
                {
                    for (int bitpos = 7; bitpos >= 0; --bitpos)
                    {


                        int bit = (int)(b >> bitpos) & 1;

                        // encode using the correct context
                        // NB: Skipping try/catch here!
                        if (use0Context)
                            model0.Encode(coder2, bit);
                        else
                            model1.Encode(coder2, bit);

                        // switch context AFTER the bit state changes
                        use0Context = (bit == 0 ? true : false);
                    }
                }
                coder2.Flush();

                Console.WriteLine("Ideal static model with context switch size is " + output2.Length);

                // The decoder looks much like the encoder. Notice how the context switch occurs only
                // after a symbol is decoded, exactly as the encoder does it.

                output2.Seek(0, SeekOrigin.Begin);


                // decode
                BinaryDecoder decoder2 = new BinaryDecoder((Stream)output2);
                SimpleStaticModel staticModel0dec = new SimpleStaticModel();
                SimpleStaticModel staticModel1dec = new SimpleStaticModel();

                staticModel0dec.SetProb(10f/128f);
                staticModel1dec.SetProb(1f - (10f/128f));

                bool use0ContextDec = true; // 0 bits come first, use the correct model!
                foreach (byte b in simpleSampleData)
                {
                    for (int bitpos = 7; bitpos >= 0; --bitpos)
                    {

                        int bit = (int)(b >> bitpos) & 1;

                        int decBit;

                        // decode using the correct context!
                        // NB: Skipping try/catch here!
                        if (use0ContextDec)
                            decBit = staticModel0dec.Decode(decoder2);
                        else
                            decBit = staticModel1dec.Decode(decoder2);

                        // the decoded bit should match the source bit for the decoder to have worked
                        if (decBit != bit)
                        {
                            Console.WriteLine("Error, static example 1 did not decode properly!");
                        }

                        // switch context when the bit state changes
                        use0ContextDec = (decBit == 0 ? true : false);
                    }
                }
            }
        }



        // Just a simple unit test, non-exhaustive! Encodes/Decodes 100 1's and 100 0's.
        private static void UnitTest()
        {
            System.IO.MemoryStream output = new System.IO.MemoryStream();

            BinaryCoder coder = new BinaryCoder((Stream)output);
            BinShiftModel adaptiveModel = new BinShiftModel();

            adaptiveModel.Inertia = 1;

            // NB: Skipping try/catch here!
            for (int i = 0; i < 100; ++i)
            {
                adaptiveModel.Encode(coder, 1);
            }
            for (int i = 0; i < 100; ++i)
            {
                adaptiveModel.Encode(coder, 0);
            }
            coder.Flush();


            output.Seek(0, SeekOrigin.Begin);

            BinaryDecoder decoder = new BinaryDecoder(output);
            BinShiftModel adaptiveDecodeModel = new BinShiftModel();
            adaptiveDecodeModel.Inertia = 1;

            bool works = true;
            //for (int j = 0; j < 10; ++j)
            //{
            for (int i = 0; i < 100; ++i)
            {
                int val = adaptiveDecodeModel.Decode(decoder);
                if (val != 1)
                {
                    Console.WriteLine("Decomp (1) error at " + i);
                    works = false;
                }
            }
            for (int i = 0; i < 100; ++i)
            {
                int val = adaptiveDecodeModel.Decode(decoder);
                if (val != 0)
                {
                    Console.WriteLine("Decomp (0) error at " + i);
                    works = false;
                }
            }
            //}

            if (!works) Console.WriteLine("Compression test had an error");
            else Console.WriteLine("Compression test worked");


            coder.Flush();
          //  Console.WriteLine("Size of compressed data = " + output.Length);
        }
    }
}
