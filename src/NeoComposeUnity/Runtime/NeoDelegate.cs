// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

namespace NeoCompose.Runtime
{
    /// <summary>NeoScript delegate with no parameters.</summary>
    public delegate TReturn NeoDelegate<out TReturn>();
    public delegate TReturn NeoDelegate<out TReturn, in P1>(P1 p1);
    public delegate TReturn NeoDelegate<out TReturn, in P1, in P2>(P1 p1, P2 p2);
    public delegate TReturn NeoDelegate<out TReturn, in P1, in P2, in P3>(P1 p1, P2 p2, P3 p3);
    public delegate TReturn NeoDelegate<out TReturn, in P1, in P2, in P3, in P4>(P1 p1, P2 p2, P3 p3, P4 p4);
    public delegate TReturn NeoDelegate<out TReturn, in P1, in P2, in P3, in P4, in P5>(P1 p1, P2 p2, P3 p3, P4 p4, P5 p5);
    public delegate TReturn NeoDelegate<out TReturn, in P1, in P2, in P3, in P4, in P5, in P6>(P1 p1, P2 p2, P3 p3, P4 p4, P5 p5, P6 p6);
    public delegate TReturn NeoDelegate<out TReturn, in P1, in P2, in P3, in P4, in P5, in P6, in P7>(P1 p1, P2 p2, P3 p3, P4 p4, P5 p5, P6 p6, P7 p7);
    public delegate TReturn NeoDelegate<out TReturn, in P1, in P2, in P3, in P4, in P5, in P6, in P7, in P8>(P1 p1, P2 p2, P3 p3, P4 p4, P5 p5, P6 p6, P7 p7, P8 p8);
    public delegate TReturn NeoDelegate<out TReturn, in P1, in P2, in P3, in P4, in P5, in P6, in P7, in P8, in P9>(P1 p1, P2 p2, P3 p3, P4 p4, P5 p5, P6 p6, P7 p7, P8 p8, P9 p9);
    public delegate TReturn NeoDelegate<out TReturn, in P1, in P2, in P3, in P4, in P5, in P6, in P7, in P8, in P9, in P10>(P1 p1, P2 p2, P3 p3, P4 p4, P5 p5, P6 p6, P7 p7, P8 p8, P9 p9, P10 p10);
    public delegate TReturn NeoDelegate<out TReturn, in P1, in P2, in P3, in P4, in P5, in P6, in P7, in P8, in P9, in P10, in P11>(P1 p1, P2 p2, P3 p3, P4 p4, P5 p5, P6 p6, P7 p7, P8 p8, P9 p9, P10 p10, P11 p11);
    public delegate TReturn NeoDelegate<out TReturn, in P1, in P2, in P3, in P4, in P5, in P6, in P7, in P8, in P9, in P10, in P11, in P12>(P1 p1, P2 p2, P3 p3, P4 p4, P5 p5, P6 p6, P7 p7, P8 p8, P9 p9, P10 p10, P11 p11, P12 p12);
    public delegate TReturn NeoDelegate<out TReturn, in P1, in P2, in P3, in P4, in P5, in P6, in P7, in P8, in P9, in P10, in P11, in P12, in P13>(P1 p1, P2 p2, P3 p3, P4 p4, P5 p5, P6 p6, P7 p7, P8 p8, P9 p9, P10 p10, P11 p11, P12 p12, P13 p13);
    public delegate TReturn NeoDelegate<out TReturn, in P1, in P2, in P3, in P4, in P5, in P6, in P7, in P8, in P9, in P10, in P11, in P12, in P13, in P14>(P1 p1, P2 p2, P3 p3, P4 p4, P5 p5, P6 p6, P7 p7, P8 p8, P9 p9, P10 p10, P11 p11, P12 p12, P13 p13, P14 p14);
    public delegate TReturn NeoDelegate<out TReturn, in P1, in P2, in P3, in P4, in P5, in P6, in P7, in P8, in P9, in P10, in P11, in P12, in P13, in P14, in P15>(P1 p1, P2 p2, P3 p3, P4 p4, P5 p5, P6 p6, P7 p7, P8 p8, P9 p9, P10 p10, P11 p11, P12 p12, P13 p13, P14 p14, P15 p15);
    public delegate TReturn NeoDelegate<out TReturn, in P1, in P2, in P3, in P4, in P5, in P6, in P7, in P8, in P9, in P10, in P11, in P12, in P13, in P14, in P15, in P16>(P1 p1, P2 p2, P3 p3, P4 p4, P5 p5, P6 p6, P7 p7, P8 p8, P9 p9, P10 p10, P11 p11, P12 p12, P13 p13, P14 p14, P15 p15, P16 p16);
}
