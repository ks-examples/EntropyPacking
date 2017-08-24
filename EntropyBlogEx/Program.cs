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

            // An efficient way to encode this is using a run-length (or dgap) scheme to code run-lengths of bits. We encode a series of values
            // to give us the starting state followed by the lengths of the runs, which would encode
            // to {28, 16, 15, 41, 3, 4, 9, 2, 2, 4, 4}, where the first 0 representing the bit starting state, and the
            // remaining bits represting the runs of the alternating states, therefore there are 
            // 28 zeros, followed by 16 ones, followed by 19 zeros, etc. This is efficiently
            // encoded using 1 bit for the initial state, and 6 bits for each subsequent value,
            // giving a grand total of 67 bits ~ about 8.375 bytes! 8.375 bytes/16 bytes = a compressed
            // size of 52%! However, it will encode to 9 bytes because everything must be, at a minimum,
            // byte-aligned. You will also need two other pieces of information: The initial bit state, and
            // the number of encoded dgaps, which in this case can all be done using a 5-bit header, bringing
            // the total output size to 9 bytes.
            // 
            //
            // That's pretty efficient. But is there another way? Enter Binary Range coding.
            // Binary Range coders work by mapping the probilities of the input bits onto the range
            // of [0...0xffffffff). With a little trickery to emit the bytes (which you can read up on).
            // Let's see what happens when we use a simple binary coder fed by a simple adaptive probability model.
            // How it works in simple: :
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

            // The result reduces the data by just a few bytes. Not nearly as good as our tailored
            // bit-packing result. This an important lesson: Using entropy alone for packing bits 
            // is NOT a silver bullet. In fact, with real-world data, entropy is probably going to 
            // make your data BIGGER. Why? Entropy works based on accurate predictions. Guess correctly,
            // you get a little benefit. Get it wrong, get punished.
            //
            // The number of bits required to encode a given symbol can be caluculated using 
            // -log2(Psym). Let's say you figure you have a 70% chance of encoding a 1 bit, but 
            // when you read the next bit you want to encode it comes up a 0. That bit is going
            // to encode to 1.74 (-log2(1-.70)) bits in the output stream - you lost almost 1 whole bit! 
            // Had you been lucky and actually read a 1 bit instead, you would have encoded it
            // with 0.51 bits (-log2(.70)), you would have saved .49 bits of space, whereas the mis-prediction
            // costs you an additional .74 bits.
            //
            // Think of it this way, the better you can predict the future, the better entropy works.
            //
            // Let's try another way to compress our data. This time, rather than be adaptive, we'll come up
            // with some fixed probabilities that work well. We can compute this ahead of time. For the above
            // dataset, we have 128 bits. 10 of these bits 'transition', that is, they are the last of a series
            // of 1 or 0, and the proceeding bit will be in the opposite state. The means that the probability
            // of the next bit being the same state as the current bit is about 92%.
            //
            // The implementation works as such: We encode 0 bits with a 8% probability of being 1,
            // and we encode 1 bits with 92% probability of being one. We can implement this using two static models:
            // One of the models is used when encoding runs of 0's. This model has a low probability of 1
            // because while you're encoding 0's there is little chance that they will encounder a 1. The other
            // model is the opposite, it encodes with a high probability of 1 because you are unlikely to 
            // encounter a zero. 
            //
            // Another important step is that you encode the current bit with the last model you used, regardless
            // of the bit's value. So if you are encoding 0's, and then you suddenly encounter a 1, you will encode
            // the 1 with the same model as the previous 0's, THEN you will switch to the 1-model. The reason for this
            // is that when decoding, you won't know ahead of time what the next decoded symbol will be, and you
            // need to follow the same rules. This switching of models during encoding/decoding is called a
            // "context switch". What is very interesting here is that, as long as you preserve the encoder
            // state, you can dynamically switch out the probability models as you go. The only requirement
            // is that whatever you do during encoding, you are able to duplicate during decoding. 
            //
            // For this example, we are going to set aside our adaptive probability model for now and just
            // use the simplest, fixed-probability type.
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

            // And just like that, it encodes to 6 bytes. In theory, it should encode
            // to 6.3 bytes, however binary range coders aren't 100% efficient, mostly
            // because they have to flush extra bits to renormalize. For this (obviously) cooked
            // data set, this is probably the most ideal representation of its compressibility in it's raw form.
            //
            // One thing that you will notice is that entropy coding comes at a price: You have to
            // do a lot more work to encode each bit of data compared to bit-packing alone. You can offset
            // this impact by minimizing your data as much as possible before using the entropy coder.

            // PART 2

            //
            // Let's look at input data. We can see that there is another interpretation
            // of the data that can be applied: It is effectively a small number of differ
            // 4-bit nibbles, namely 0x0, 0x1, 0xe, 0xc, and 0xf. Not only that, but there are long
            // runs of each symbol. We can create a type of gzip-lite to compress these.
            // The first thing we can notice is that some of values can be encoded just once,
            // 0x0 and 0xf are common, so there is a large gain to be had by encoding these one time
            // and then referencing back to them using a short index code and a run-length to handle
            // repeat cases.
            //
            // Let's define the following. We encode 1 bit. This bit denotes that the next value is either
            // a literal value (ie. we just encode the nibble itself), or it is a reference to a previous literal
            // followed by a run-length. We will call it the 'literal flag'. Each time we encode a literal, 
            // we put it in an array of literals, which can then be retrieved via their index value, we refer to this as the 
            // literal dictionary.  We define the literal flag as 1 when a literal value follows, and 0 when a reference follows.
            // References are encoded as two values. The first is an index into the literal array (or dictionary), the second is
            // a run-length code - how often the nibble from the dictionary is repeated. We can then use these rules to 
            // build a type of pseudo code of what the compressed output stream would look like:
            /* (original data)
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
             */
            // { [literal_flag=1, literal=0x0],         -- encode 0x0, assign to index 0 in dictionary
            //   [literal_flag=0, index=0, repeat=6],   -- encode 6 0x0 
            //   [literal_flag=1, literal=0xf],         -- encode 0xf, assign to index 1 in dictionary
            //   [literal_flag=0, index=1, repeat=3],   -- encode 3 0xf
            //   [literal_flag=0, index=0, repeat=3],   -- encode 3 0x0
            //   [literal_flag=1, literal=0x1],         -- encode 0x1, assign to index 2 in dictionary
            //   [literal_flag=0, index=1, repeat=10]   -- encode 10 0xf
            //   [literal_flag=0, index=2, repeat=1]    -- encode 1 0x1
            //   [literal_flag=1, literal=0xe]          -- encode 0xe, assign to index 3 in dictionary
            //   [literal_flag=0, index=0, repeat=2]    -- encode 2 0x0
            //   [literal_flag=1, literal=0xc]          -- encode 0xc, assign to index 4 in dictionary
            //   [literal_flag=0, index=1, repeat=1]    -- encode 1 0xf
            //   [literal_flag=0, index=0, repeat=1]    -- encode 1 0x0
            // 
            // If we just count the bits, where 1 bit is used for literal_flag, 4 bits for the literals, 3 bits for the index, and 4 bits
            // for the length, we get a total bit-packed size of [5+8+5+8+8+5+8+8+5+8+5+8+8 = 89 bits (11.125 bytes). 
            // 
            // We can still make it a bit better. In two instances, the same repeat code is used twice in a row. We can add a flag to the repeat code
            // the signals resuing the previous repeat value. This expands all codes by 1 bit, but shortens 2 of the repeat codes from
            // 4 bits to 1 bit. This leaves us with 6 full-size repeat codes, and 2 short repeat codes, adding 6 bits but saving an additional
            // 8 bits, for a net gain of 2 bits. The data is now packs to 10.875 bytes. 


            // TODO: FIX
            //We can also shorten the repeat codes themselves. 
            // First, we can subtract 1 from each value, as a repeat code of 0 has no meaning. This means repeats of 1 or 2 require just 1 bit. 2 of
            // the 8 repeat codes have values that only require 2 bits, but we are encoding them using 4 bits. We can add a length flag denoting
            // if we are using 2-bit or 4-bit repeat lengths. This reduces 6 repeat codes to 3 bits, and expand the remaining 2 by 1 bit.
            // This saves us another 10 bits. 
            //
            // The next piece of data is the index value. It can be packed more efficiently because we are using 3 bits, however we really only 
            // ever need 2, and most of the time we are looking at index 0 or 1. We can add a 1-bit flag to the index value to state whether 
            // the index uses 1 bit or 3 bits. Doing so makes 7 of the 8 index values drop from 3 bits to 2, and expands one of 3 bits to 4, 
            // saving us another 7 bits. The data now packs to 9.75 bytes. The psuedo code is now more complicated:
            // 
            // { [literal_flag=1, literal=0x0],                                                     (5 bits)
            //   [literal_flag=0, index_size=0, index=0, reuse_repeat=0, repeat_size=1, repeat=5],  (9 bits)
            //   [literal_flag=1, literal=0xf],                                                     (5 bits)
            //   [literal_flag=0, index_size=0, index=1, reuse_repeat=0, repeat_size=0, repeat=2],  (9 bits)
            //   [literal_flag=0, index_size=0, index=0, reuse_repeat=1],                           (4 bits)
            //   [literal_flag=1, literal=0x1],                                                     (5 bits)
            //   [literal_flag=0, index_size=0, index=1, reuse_repeat=0, repeat_size=1, repeat=9],  (9 bits)
            //   [literal_flag=0, index_size=1, index=2, reuse_repeat=0, repeat_size=0, repeat=0],  (8 bits)
            //   [literal_flag=1, literal=0xe],                                                     (5 bits)
            //   [literal_flag=0, index_size=0, index=0, reuse_repeat=0, repeat_size=0, repeat=1],  (7 bits)
            //   [literal_flag=1, literal=0xc],                                                     (5 bits)
            //   [literal_flag=0, index_size=0, index=1, reuse_repeat=0, repeat_size=0, repeat=0],  (6 bits)
            //   [literal_flag=0, index_size=0, index=0, reuse_repeat=1]                            (4 bits)
            //
            // Total: 89 bits -> 10.125 bytes
            // 
            // Now it gets interesting. We want to use entropy to see if we can reduce the size of the data even further.
            // Now, in order for entropy encoding to gain us anything, we have to have an accurate model that gives us
            // a good prediction of the value we are about to encode. Our model is heavily influenced by an analysis of 
            // the data we want to compress. We start by looking at the statitics of each field, and construct our model
            // based on it.
            //
            // Let's start by looking at the literal_flag field. You can see that it is more often 0 than it is 1, but
            // what's more interesting is that the probability of encoding a 0 after a 1 is extremely high. One approach
            // is to simply assume that the next proceeding literal_flag value is 0 if the current was decoded as 1, however
            // that might be too aggressive, and a proper decode can fail if the source data changes in the future.
            // We also note that we only encode at most 2 literal_flags with a value of 0, which is a fact we can exploit,
            // but we will choose not to in order to maintain some degree of future proofing. 
            // 
            // The next field we look at is the 'literal' field. These are effectively unique, and are encoded with a
            // probability of a fixed 0.5 per bit, which encodes 1 bit using the space of 1 bit - it is effectively just
            // bit-packing, however it is embedded in the entropy stream and thus does not require any special switching.
            //
            // The index_size field is almost always 0, we can encode this using a fixed probability of (1/8), or 0.125,
            // since 1 of the 8 values in the stream is a 1, however that may not be the case if the data changes in the 
            // future. We can adapt the probability of it being 1 over time, and start with a bias towards encoding a 0. 
            // This makes sense for the data as presented, but may not always make be a benefit.
            //
            // The index itself is simply a value that is encoded with either 1 or 3 bits. 
            {
                // converting simpleSamplesData to dgaps yields this transform
                System.IO.MemoryStream output2 = new System.IO.MemoryStream();
                BinaryCoder coder2 = new BinaryCoder((Stream)output2);
                SimpleStaticModel[] numberModel = new SimpleStaticModel[6];
                for (int i=0;i<numberModel.Length;++i)
                {
                    numberModel[i] = new SimpleStaticModel();

                    // let's just set an assumed probability here. We could of course
                    numberModel[i].SetProb(1f / (float)(2<<i));
                }

                int nibbleIdx = 0;
                List<byte> dictionary = new List<byte>();

                foreach (byte b in simpleSampleData)
                {
                    byte nibble = 0x00;

                    // get the correct nibble
                    if (nibbleIdx %2 == 0)
                    {
                        nibble = (byte)((b >> 4) & 0x0f);
                    } else
                    {
                        nibble = (byte)(b & 0x0f);
                    }
                    nibbleIdx++;

                    int location = dictionary.FindIndex(delegate (byte item) { return (item == nibble); });

                    if (location == -1)
                    {
                        // Not found. Add to dictionary. encode as literal.
                        dictionary.Add(nibble);

                        // encode
                    } else
                    {
                        // Found. Count repeats.

                    }
                }
                coder2.Flush();

               Console.WriteLine("Number model output size is " + output2.Length);
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
