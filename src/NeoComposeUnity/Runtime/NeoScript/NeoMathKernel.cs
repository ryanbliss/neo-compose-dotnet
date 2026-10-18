/*
 * Derived from FDLIBM 5.3, Sun Microsystems, Inc.
 * Copyright (C) 1993, 2004 by Sun Microsystems, Inc. All rights reserved.
 * Permission to use, copy, modify, and distribute this software is freely
 * granted, provided that this notice is preserved.
 * Source files and SHA-256 hashes are listed in fdlibm-sources.json.
 * Binary64 operation order and constants are intentional.
 */
using System;
namespace NeoCompose.Runtime.NeoScript
{
    internal static class NeoMathKernel
    {
        private static int highWord(double x) => (int)(BitConverter.DoubleToInt64Bits(x) >> 32);
        private static int lowWord(double x) => unchecked((int)BitConverter.DoubleToInt64Bits(x));
        private static double withHighWord(double x, int w) => BitConverter.Int64BitsToDouble(((long)w << 32) | (BitConverter.DoubleToInt64Bits(x) & 0xffffffffL));
        private static double withLowWord(double x, int w) => BitConverter.Int64BitsToDouble((BitConverter.DoubleToInt64Bits(x) & unchecked((long)0xffffffff00000000UL)) | (uint)w);
        private static double copysign(double x, double y) => withHighWord(x, (highWord(x) & 0x7fffffff) | (highWord(y) & unchecked((int)0x80000000)));

        [ThreadStatic] private static double[]? s_sin_y;
        [ThreadStatic] private static double[]? s_cos_y;
        [ThreadStatic] private static double[]? s_tan_y;
        [ThreadStatic] private static double[]? e_rem_pio2_tx;
        [ThreadStatic] private static int[]? k_rem_pio2_iq;
        [ThreadStatic] private static double[]? k_rem_pio2_f;
        [ThreadStatic] private static double[]? k_rem_pio2_fq;
        [ThreadStatic] private static double[]? k_rem_pio2_q;
        // e_pow.c
        private static readonly double[] e_pow_bp = { 1.0, 1.5 };
        private static readonly double[] e_pow_dp_h = { 0.0, 5.84962487220764160156e-01 };
        private static readonly double[] e_pow_dp_l = { 0.0, 1.35003920212974897128e-08 };
        private const double e_pow_zero = 0.0;
        private const double e_pow_one = 1.0;
        private const double e_pow_two = 2.0;
        private const double e_pow_two53 = 9007199254740992.0;
        private const double e_pow_huge = 1.0e300;
        private const double e_pow_tiny = 1.0e-300;
        private const double e_pow_L1 = 5.99999999999994648725e-01;
        private const double e_pow_L2 = 4.28571428578550184252e-01;
        private const double e_pow_L3 = 3.33333329818377432918e-01;
        private const double e_pow_L4 = 2.72728123808534006489e-01;
        private const double e_pow_L5 = 2.30660745775561754067e-01;
        private const double e_pow_L6 = 2.06975017800338417784e-01;
        private const double e_pow_P1 = 1.66666666666666019037e-01;
        private const double e_pow_P2 = (-2.77777777770155933842e-03);
        private const double e_pow_P3 = 6.61375632143793436117e-05;
        private const double e_pow_P4 = (-1.65339022054652515390e-06);
        private const double e_pow_P5 = 4.13813679705723846039e-08;
        private const double e_pow_lg2 = 6.93147180559945286227e-01;
        private const double e_pow_lg2_h = 6.93147182464599609375e-01;
        private const double e_pow_lg2_l = (-1.90465429995776804525e-09);
        private const double e_pow_ovt = 8.0085662595372944372e-0017;
        private const double e_pow_cp = 9.61796693925975554329e-01;
        private const double e_pow_cp_h = 9.61796700954437255859e-01;
        private const double e_pow_cp_l = (-7.02846165095275826516e-09);
        private const double e_pow_ivln2 = 1.44269504088896338700e+00;
        private const double e_pow_ivln2_h = 1.44269502162933349609e+00;
        private const double e_pow_ivln2_l = 1.92596299112661746887e-08;
        private static double kernel___ieee754_pow(double x, double y)
        {
            double z = 0;
            double ax = 0;
            double z_h = 0;
            double z_l = 0;
            double p_h = 0;
            double p_l = 0;
            double y1 = 0;
            double t1 = 0;
            double t2 = 0;
            double r = 0;
            double s = 0;
            double t = 0;
            double u = 0;
            double v = 0;
            double w = 0;
            int i = 0;
            int j = 0;
            int k = 0;
            int yisint = 0;
            int n = 0;
            int hx = 0;
            int hy = 0;
            int ix = 0;
            int iy = 0;
            uint lx = 0;
            uint ly = 0;
            hx = highWord(x);
            lx = unchecked((uint)(lowWord(x)));
            hy = highWord(y);
            ly = unchecked((uint)(lowWord(y)));
            ix = ((hx) & (0x7fffffff));
            iy = ((hy) & (0x7fffffff));
            if ((((unchecked((uint)((iy))) | (ly))) == unchecked((uint)((0)))))
            {
                return e_pow_one;
            }
            if ((((((ix) > (0x7ff00000)) || (((ix) == (0x7ff00000)) && ((lx) != unchecked((uint)((0)))))) || ((iy) > (0x7ff00000))) || (((iy) == (0x7ff00000)) && ((ly) != unchecked((uint)((0)))))))
            {
                return ((x) + (y));
            }
            yisint = 0;
            if (((hx) < (0)))
            {
                if (((iy) >= (0x43400000)))
                {
                    yisint = 2;
                }
                else
                {
                    if (((iy) >= (0x3ff00000)))
                    {
                        k = ((((iy) >> (20))) - (0x3ff));
                        if (((k) > (20)))
                        {
                            j = unchecked((int)(((ly) >> (((52) - (k))))));
                            if ((unchecked((uint)((((j) << (((52) - (k))))))) == (ly)))
                            {
                                yisint = ((2) - (((j) & (1))));
                            }
                        }
                        else
                        {
                            if (((ly) == unchecked((uint)((0)))))
                            {
                                j = ((iy) >> (((20) - (k))));
                                if (((((j) << (((20) - (k))))) == (iy)))
                                {
                                    yisint = ((2) - (((j) & (1))));
                                }
                            }
                        }
                    }
                }
            }
            if (((ly) == unchecked((uint)((0)))))
            {
                if (((iy) == (0x7ff00000)))
                {
                    if ((((unchecked((uint)((((ix) - (0x3ff00000))))) | (lx))) == unchecked((uint)((0)))))
                    {
                        return ((y) - (y));
                    }
                    else
                    {
                        if (((ix) >= (0x3ff00000)))
                        {
                            return (((hy) >= (0)) ? y : e_pow_zero);
                        }
                        else
                        {
                            return (((hy) < (0)) ? (-y) : e_pow_zero);
                        }
                    }
                }
                if (((iy) == (0x3ff00000)))
                {
                    if (((hy) < (0)))
                    {
                        return ((e_pow_one) / (x));
                    }
                    else
                    {
                        return x;
                    }
                }
                if (((hy) == (0x40000000)))
                {
                    return ((x) * (x));
                }
                if (((hy) == (0x3fe00000)))
                {
                    if (((hx) >= (0)))
                    {
                        return Math.Sqrt(x);
                    }
                }
            }
            ax = Math.Abs(x);
            if (((lx) == unchecked((uint)((0)))))
            {
                if (((((ix) == (0x7ff00000)) || ((ix) == (0))) || ((ix) == (0x3ff00000))))
                {
                    z = ax;
                    if (((hy) < (0)))
                    {
                        z = ((e_pow_one) / (z));
                    }
                    if (((hx) < (0)))
                    {
                        if (((((((ix) - (0x3ff00000))) | (yisint))) == (0)))
                        {
                            z = ((((z) - (z))) / (((z) - (z))));
                        }
                        else
                        {
                            if (((yisint) == (1)))
                            {
                                z = (-z);
                            }
                        }
                    }
                    return z;
                }
            }
            n = ((((hx) >> (31))) + (1));
            if (((((n) | (yisint))) == (0)))
            {
                return ((((x) - (x))) / (((x) - (x))));
            }
            s = e_pow_one;
            if (((((n) | (((yisint) - (1))))) == (0)))
            {
                s = (-e_pow_one);
            }
            if (((iy) > (0x41e00000)))
            {
                if (((iy) > (0x43f00000)))
                {
                    if (((ix) <= (0x3fefffff)))
                    {
                        return (((hy) < (0)) ? ((e_pow_huge) * (e_pow_huge)) : ((e_pow_tiny) * (e_pow_tiny)));
                    }
                    if (((ix) >= (0x3ff00000)))
                    {
                        return (((hy) > (0)) ? ((e_pow_huge) * (e_pow_huge)) : ((e_pow_tiny) * (e_pow_tiny)));
                    }
                }
                if (((ix) < (0x3fefffff)))
                {
                    return (((hy) < (0)) ? ((((s) * (e_pow_huge))) * (e_pow_huge)) : ((((s) * (e_pow_tiny))) * (e_pow_tiny)));
                }
                if (((ix) > (0x3ff00000)))
                {
                    return (((hy) > (0)) ? ((((s) * (e_pow_huge))) * (e_pow_huge)) : ((((s) * (e_pow_tiny))) * (e_pow_tiny)));
                }
                t = ((ax) - (e_pow_one));
                w = ((((t) * (t))) * (((0.5) - (((t) * (((0.3333333333333333333333) - (((t) * (0.25))))))))));
                u = ((e_pow_ivln2_h) * (t));
                v = ((((t) * (e_pow_ivln2_l))) - (((w) * (e_pow_ivln2))));
                t1 = ((u) + (v));
                t1 = withLowWord(t1, 0);
                t2 = ((v) - (((t1) - (u))));
            }
            else
            {
                double ss = 0;
                double s2 = 0;
                double s_h = 0;
                double s_l = 0;
                double t_h = 0;
                double t_l = 0;
                n = 0;
                if (((ix) < (0x00100000)))
                {
                    ax = (ax * e_pow_two53);
                    n = (n - 53);
                    ix = highWord(ax);
                }
                n = (n + ((((ix) >> (20))) - (0x3ff)));
                j = ((ix) & (0x000fffff));
                ix = ((j) | (0x3ff00000));
                if (((j) <= (0x3988E)))
                {
                    k = 0;
                }
                else
                {
                    if (((j) < (0xBB67A)))
                    {
                        k = 1;
                    }
                    else
                    {
                        k = 0;
                        n = (n + 1);
                        ix = (ix - 0x00100000);
                    }
                }
                ax = withHighWord(ax, ix);
                u = ((ax) - (e_pow_bp[k]));
                v = ((e_pow_one) / (((ax) + (e_pow_bp[k]))));
                ss = ((u) * (v));
                s_h = ss;
                s_h = withLowWord(s_h, 0);
                t_h = e_pow_zero;
                t_h = withHighWord(t_h, ((((((((ix) >> (1))) | (0x20000000))) + (0x00080000))) + (((k) << (18)))));
                t_l = ((ax) - (((t_h) - (e_pow_bp[k]))));
                s_l = ((v) * (((((u) - (((s_h) * (t_h))))) - (((s_h) * (t_l))))));
                s2 = ((ss) * (ss));
                r = ((((s2) * (s2))) * (((e_pow_L1) + (((s2) * (((e_pow_L2) + (((s2) * (((e_pow_L3) + (((s2) * (((e_pow_L4) + (((s2) * (((e_pow_L5) + (((s2) * (e_pow_L6))))))))))))))))))))));
                r = (r + ((s_l) * (((s_h) + (ss)))));
                s2 = ((s_h) * (s_h));
                t_h = ((((3.0) + (s2))) + (r));
                t_h = withLowWord(t_h, 0);
                t_l = ((r) - (((((t_h) - (3.0))) - (s2))));
                u = ((s_h) * (t_h));
                v = ((((s_l) * (t_h))) + (((t_l) * (ss))));
                p_h = ((u) + (v));
                p_h = withLowWord(p_h, 0);
                p_l = ((v) - (((p_h) - (u))));
                z_h = ((e_pow_cp_h) * (p_h));
                z_l = ((((((e_pow_cp_l) * (p_h))) + (((p_l) * (e_pow_cp))))) + (e_pow_dp_l[k]));
                t = ((double)(n));
                t1 = ((((((z_h) + (z_l))) + (e_pow_dp_h[k]))) + (t));
                t1 = withLowWord(t1, 0);
                t2 = ((z_l) - (((((((t1) - (t))) - (e_pow_dp_h[k]))) - (z_h))));
            }
            y1 = y;
            y1 = withLowWord(y1, 0);
            p_l = ((((((y) - (y1))) * (t1))) + (((y) * (t2))));
            p_h = ((y1) * (t1));
            z = ((p_l) + (p_h));
            j = highWord(z);
            i = lowWord(z);
            if (((j) >= (0x40900000)))
            {
                if (((((((j) - (0x40900000))) | (i))) != (0)))
                {
                    return ((((s) * (e_pow_huge))) * (e_pow_huge));
                }
                else
                {
                    if (((((p_l) + (e_pow_ovt))) > (((z) - (p_h)))))
                    {
                        return ((((s) * (e_pow_huge))) * (e_pow_huge));
                    }
                }
            }
            else
            {
                if (((((j) & (0x7fffffff))) >= (0x4090cc00)))
                {
                    if ((((((unchecked((uint)((j))) - (0xc090cc00))) | unchecked((uint)((i))))) != unchecked((uint)((0)))))
                    {
                        return ((((s) * (e_pow_tiny))) * (e_pow_tiny));
                    }
                    else
                    {
                        if (((p_l) <= (((z) - (p_h)))))
                        {
                            return ((((s) * (e_pow_tiny))) * (e_pow_tiny));
                        }
                    }
                }
            }
            i = ((j) & (0x7fffffff));
            k = ((((i) >> (20))) - (0x3ff));
            n = 0;
            if (((i) > (0x3fe00000)))
            {
                n = ((j) + (((0x00100000) >> (((k) + (1))))));
                k = ((((((n) & (0x7fffffff))) >> (20))) - (0x3ff));
                t = e_pow_zero;
                t = withHighWord(t, ((n) & ((~((0x000fffff) >> (k))))));
                n = ((((((n) & (0x000fffff))) | (0x00100000))) >> (((20) - (k))));
                if (((j) < (0)))
                {
                    n = (-n);
                }
                p_h = (p_h - t);
            }
            t = ((p_l) + (p_h));
            t = withLowWord(t, 0);
            u = ((t) * (e_pow_lg2_h));
            v = ((((((p_l) - (((t) - (p_h))))) * (e_pow_lg2))) + (((t) * (e_pow_lg2_l))));
            z = ((u) + (v));
            w = ((v) - (((z) - (u))));
            t = ((z) * (z));
            t1 = ((z) - (((t) * (((e_pow_P1) + (((t) * (((e_pow_P2) + (((t) * (((e_pow_P3) + (((t) * (((e_pow_P4) + (((t) * (e_pow_P5))))))))))))))))))));
            r = ((((((z) * (t1))) / (((t1) - (e_pow_two))))) - (((w) + (((z) * (w))))));
            z = ((e_pow_one) - (((r) - (z))));
            j = highWord(z);
            j = (j + ((n) << (20)));
            if (((((j) >> (20))) <= (0)))
            {
                z = kernel_scalbn(z, n);
            }
            else
            {
                z = withHighWord(z, (highWord(z) + ((n) << (20))));
            }
            return ((s) * (z));
        }
        // e_exp.c
        private const double e_exp_one = 1.0;
        private static readonly double[] e_exp_halF = { 0.5, (-0.5) };
        private const double e_exp_huge = 1.0e+300;
        private const double e_exp_twom1000 = 9.33263618503218878990e-302;
        private const double e_exp_o_threshold = 7.09782712893383973096e+02;
        private const double e_exp_u_threshold = (-7.45133219101941108420e+02);
        private static readonly double[] e_exp_ln2HI = { 6.93147180369123816490e-01, (-6.93147180369123816490e-01) };
        private static readonly double[] e_exp_ln2LO = { 1.90821492927058770002e-10, (-1.90821492927058770002e-10) };
        private const double e_exp_invln2 = 1.44269504088896338700e+00;
        private const double e_exp_P1 = 1.66666666666666019037e-01;
        private const double e_exp_P2 = (-2.77777777770155933842e-03);
        private const double e_exp_P3 = 6.61375632143793436117e-05;
        private const double e_exp_P4 = (-1.65339022054652515390e-06);
        private const double e_exp_P5 = 4.13813679705723846039e-08;
        private static double kernel___ieee754_exp(double x)
        {
            double y = 0;
            double hi = 0;
            double lo = 0;
            double c = 0;
            double t = 0;
            int k = 0;
            int xsb = 0;
            uint hx = 0;
            hx = unchecked((uint)(highWord(x)));
            xsb = unchecked((int)(((((hx) >> (31))) & unchecked((uint)((1))))));
            hx = unchecked((uint)((hx & 0x7fffffff)));
            if (((hx) >= unchecked((uint)((0x40862E42)))))
            {
                if (((hx) >= unchecked((uint)((0x7ff00000)))))
                {
                    if (((((((hx) & unchecked((uint)((0xfffff))))) | unchecked((uint)((lowWord(x)))))) != unchecked((uint)((0)))))
                    {
                        return ((x) + (x));
                    }
                    else
                    {
                        return (((xsb) == (0)) ? x : 0.0);
                    }
                }
                if (((x) > (e_exp_o_threshold)))
                {
                    return ((e_exp_huge) * (e_exp_huge));
                }
                if (((x) < (e_exp_u_threshold)))
                {
                    return ((e_exp_twom1000) * (e_exp_twom1000));
                }
            }
            if (((hx) > unchecked((uint)((0x3fd62e42)))))
            {
                if (((hx) < unchecked((uint)((0x3FF0A2B2)))))
                {
                    hi = ((x) - (e_exp_ln2HI[xsb]));
                    lo = e_exp_ln2LO[xsb];
                    k = ((((1) - (xsb))) - (xsb));
                }
                else
                {
                    k = unchecked((int)(((((e_exp_invln2) * (x))) + (e_exp_halF[xsb]))));
                    t = k;
                    hi = ((x) - (((t) * (e_exp_ln2HI[0]))));
                    lo = ((t) * (e_exp_ln2LO[0]));
                }
                x = ((hi) - (lo));
            }
            else
            {
                if (((hx) < unchecked((uint)((0x3e300000)))))
                {
                    if (((((e_exp_huge) + (x))) > (e_exp_one)))
                    {
                        return ((e_exp_one) + (x));
                    }
                }
                else
                {
                    k = 0;
                }
            }
            t = ((x) * (x));
            c = ((x) - (((t) * (((e_exp_P1) + (((t) * (((e_exp_P2) + (((t) * (((e_exp_P3) + (((t) * (((e_exp_P4) + (((t) * (e_exp_P5))))))))))))))))))));
            if (((k) == (0)))
            {
                return ((e_exp_one) - (((((((x) * (c))) / (((c) - (2.0))))) - (x))));
            }
            else
            {
                y = ((e_exp_one) - (((((lo) - (((((x) * (c))) / (((2.0) - (c))))))) - (hi))));
            }
            if (((k) >= ((-1021))))
            {
                y = withHighWord(y, (highWord(y) + ((k) << (20))));
                return y;
            }
            else
            {
                y = withHighWord(y, (highWord(y) + ((((k) + (1000))) << (20))));
                return ((y) * (e_exp_twom1000));
            }
        }
        // e_log.c
        private const double e_log_ln2_hi = 6.93147180369123816490e-01;
        private const double e_log_ln2_lo = 1.90821492927058770002e-10;
        private const double e_log_two54 = 1.80143985094819840000e+16;
        private const double e_log_Lg1 = 6.666666666666735130e-01;
        private const double e_log_Lg2 = 3.999999999940941908e-01;
        private const double e_log_Lg3 = 2.857142874366239149e-01;
        private const double e_log_Lg4 = 2.222219843214978396e-01;
        private const double e_log_Lg5 = 1.818357216161805012e-01;
        private const double e_log_Lg6 = 1.531383769920937332e-01;
        private const double e_log_Lg7 = 1.479819860511658591e-01;
        private const double e_log_zero = 0.0;
        private static double kernel___ieee754_log(double x)
        {
            double hfsq = 0;
            double f = 0;
            double s = 0;
            double z = 0;
            double R = 0;
            double w = 0;
            double t1 = 0;
            double t2 = 0;
            double dk = 0;
            int k = 0;
            int hx = 0;
            int i = 0;
            int j = 0;
            uint lx = 0;
            hx = highWord(x);
            lx = unchecked((uint)(lowWord(x)));
            k = 0;
            if (((hx) < (0x00100000)))
            {
                if ((((unchecked((uint)((((hx) & (0x7fffffff))))) | (lx))) == unchecked((uint)((0)))))
                {
                    return (((-e_log_two54)) / (e_log_zero));
                }
                if (((hx) < (0)))
                {
                    return ((((x) - (x))) / (e_log_zero));
                }
                k = (k - 54);
                x = (x * e_log_two54);
                hx = highWord(x);
            }
            if (((hx) >= (0x7ff00000)))
            {
                return ((x) + (x));
            }
            k = (k + ((((hx) >> (20))) - (1023)));
            hx = (hx & 0x000fffff);
            i = ((((hx) + (0x95f64))) & (0x100000));
            x = withHighWord(x, ((hx) | (((i) ^ (0x3ff00000)))));
            k = (k + ((i) >> (20)));
            f = ((x) - (1.0));
            if (((((0x000fffff) & (((2) + (hx))))) < (3)))
            {
                if (((f) == (e_log_zero)))
                {
                    if (((k) == (0)))
                    {
                        return e_log_zero;
                    }
                    else
                    {
                        dk = ((double)(k));
                        return ((((dk) * (e_log_ln2_hi))) + (((dk) * (e_log_ln2_lo))));
                    }
                }
                R = ((((f) * (f))) * (((0.5) - (((0.33333333333333333) * (f))))));
                if (((k) == (0)))
                {
                    return ((f) - (R));
                }
                else
                {
                    dk = ((double)(k));
                    return ((((dk) * (e_log_ln2_hi))) - (((((R) - (((dk) * (e_log_ln2_lo))))) - (f))));
                }
            }
            s = ((f) / (((2.0) + (f))));
            dk = ((double)(k));
            z = ((s) * (s));
            i = ((hx) - (0x6147a));
            w = ((z) * (z));
            j = ((0x6b851) - (hx));
            t1 = ((w) * (((e_log_Lg2) + (((w) * (((e_log_Lg4) + (((w) * (e_log_Lg6))))))))));
            t2 = ((z) * (((e_log_Lg1) + (((w) * (((e_log_Lg3) + (((w) * (((e_log_Lg5) + (((w) * (e_log_Lg7))))))))))))));
            i = (i | j);
            R = ((t2) + (t1));
            if (((i) > (0)))
            {
                hfsq = ((((0.5) * (f))) * (f));
                if (((k) == (0)))
                {
                    return ((f) - (((hfsq) - (((s) * (((hfsq) + (R))))))));
                }
                else
                {
                    return ((((dk) * (e_log_ln2_hi))) - (((((hfsq) - (((((s) * (((hfsq) + (R))))) + (((dk) * (e_log_ln2_lo))))))) - (f))));
                }
            }
            else
            {
                if (((k) == (0)))
                {
                    return ((f) - (((s) * (((f) - (R))))));
                }
                else
                {
                    return ((((dk) * (e_log_ln2_hi))) - (((((((s) * (((f) - (R))))) - (((dk) * (e_log_ln2_lo))))) - (f))));
                }
            }
        }
        // e_log10.c
        private const double e_log10_two54 = 1.80143985094819840000e+16;
        private const double e_log10_ivln10 = 4.34294481903251816668e-01;
        private const double e_log10_log10_2hi = 3.01029995663611771306e-01;
        private const double e_log10_log10_2lo = 3.69423907715893078616e-13;
        private const double e_log10_zero = 0.0;
        private static double kernel___ieee754_log10(double x)
        {
            double y = 0;
            double z = 0;
            int i = 0;
            int k = 0;
            int hx = 0;
            uint lx = 0;
            hx = highWord(x);
            lx = unchecked((uint)(lowWord(x)));
            k = 0;
            if (((hx) < (0x00100000)))
            {
                if ((((unchecked((uint)((((hx) & (0x7fffffff))))) | (lx))) == unchecked((uint)((0)))))
                {
                    return (((-e_log10_two54)) / (e_log10_zero));
                }
                if (((hx) < (0)))
                {
                    return ((((x) - (x))) / (e_log10_zero));
                }
                k = (k - 54);
                x = (x * e_log10_two54);
                hx = highWord(x);
            }
            if (((hx) >= (0x7ff00000)))
            {
                return ((x) + (x));
            }
            k = (k + ((((hx) >> (20))) - (1023)));
            i = unchecked((int)(((((unchecked((uint)(k))) & (0x80000000))) >> (31))));
            hx = ((((hx) & (0x000fffff))) | (((((0x3ff) - (i))) << (20))));
            y = ((double)(((k) + (i))));
            x = withHighWord(x, hx);
            z = ((((y) * (e_log10_log10_2lo))) + (((e_log10_ivln10) * (kernel___ieee754_log(x)))));
            return ((z) + (((y) * (e_log10_log10_2hi))));
        }
        // s_sin.c
        private static double kernel_sin(double x)
        {
            double[] y = (s_sin_y ??= new double[2]);
            double z = 0.0;
            int n = 0;
            int ix = 0;
            ix = highWord(x);
            ix = (ix & 0x7fffffff);
            if (((ix) <= (0x3fe921fb)))
            {
                return kernel___kernel_sin(x, z, 0);
            }
            else
            {
                if (((ix) >= (0x7ff00000)))
                {
                    return ((x) - (x));
                }
                else
                {
                    n = kernel___ieee754_rem_pio2(x, y);
                    switch (((n) & (3)))
                    {
                        case 0:
                            return kernel___kernel_sin(y[0], y[1], 1);
                        case 1:
                            return kernel___kernel_cos(y[0], y[1]);
                        case 2:
                            return (-kernel___kernel_sin(y[0], y[1], 1));
                        default:
                            return (-kernel___kernel_cos(y[0], y[1]));
                    }
                }
            }
        }
        // s_cos.c
        private static double kernel_cos(double x)
        {
            double[] y = (s_cos_y ??= new double[2]);
            double z = 0.0;
            int n = 0;
            int ix = 0;
            ix = highWord(x);
            ix = (ix & 0x7fffffff);
            if (((ix) <= (0x3fe921fb)))
            {
                return kernel___kernel_cos(x, z);
            }
            else
            {
                if (((ix) >= (0x7ff00000)))
                {
                    return ((x) - (x));
                }
                else
                {
                    n = kernel___ieee754_rem_pio2(x, y);
                    switch (((n) & (3)))
                    {
                        case 0:
                            return kernel___kernel_cos(y[0], y[1]);
                        case 1:
                            return (-kernel___kernel_sin(y[0], y[1], 1));
                        case 2:
                            return (-kernel___kernel_cos(y[0], y[1]));
                        default:
                            return kernel___kernel_sin(y[0], y[1], 1);
                    }
                }
            }
        }
        // s_tan.c
        private static double kernel_tan(double x)
        {
            double[] y = (s_tan_y ??= new double[2]);
            double z = 0.0;
            int n = 0;
            int ix = 0;
            ix = highWord(x);
            ix = (ix & 0x7fffffff);
            if (((ix) <= (0x3fe921fb)))
            {
                return kernel___kernel_tan(x, z, 1);
            }
            else
            {
                if (((ix) >= (0x7ff00000)))
                {
                    return ((x) - (x));
                }
                else
                {
                    n = kernel___ieee754_rem_pio2(x, y);
                    return kernel___kernel_tan(y[0], y[1], ((1) - (((((n) & (1))) << (1)))));
                }
            }
        }
        // e_asin.c
        private const double e_asin_one = 1.00000000000000000000e+00;
        private const double e_asin_huge = 1.000e+300;
        private const double e_asin_pio2_hi = 1.57079632679489655800e+00;
        private const double e_asin_pio2_lo = 6.12323399573676603587e-17;
        private const double e_asin_pio4_hi = 7.85398163397448278999e-01;
        private const double e_asin_pS0 = 1.66666666666666657415e-01;
        private const double e_asin_pS1 = (-3.25565818622400915405e-01);
        private const double e_asin_pS2 = 2.01212532134862925881e-01;
        private const double e_asin_pS3 = (-4.00555345006794114027e-02);
        private const double e_asin_pS4 = 7.91534994289814532176e-04;
        private const double e_asin_pS5 = 3.47933107596021167570e-05;
        private const double e_asin_qS1 = (-2.40339491173441421878e+00);
        private const double e_asin_qS2 = 2.02094576023350569471e+00;
        private const double e_asin_qS3 = (-6.88283971605453293030e-01);
        private const double e_asin_qS4 = 7.70381505559019352791e-02;
        private static double kernel___ieee754_asin(double x)
        {
            double t = 0;
            double w = 0;
            double p = 0;
            double q = 0;
            double c = 0;
            double r = 0;
            double s = 0;
            int hx = 0;
            int ix = 0;
            hx = highWord(x);
            ix = ((hx) & (0x7fffffff));
            if (((ix) >= (0x3ff00000)))
            {
                if (((((((ix) - (0x3ff00000))) | (lowWord(x)))) == (0)))
                {
                    return ((((x) * (e_asin_pio2_hi))) + (((x) * (e_asin_pio2_lo))));
                }
                return ((((x) - (x))) / (((x) - (x))));
            }
            else
            {
                if (((ix) < (0x3fe00000)))
                {
                    if (((ix) < (0x3e400000)))
                    {
                        if (((((e_asin_huge) + (x))) > (e_asin_one)))
                        {
                            return x;
                        }
                    }
                    else
                    {
                        t = ((x) * (x));
                    }
                    p = ((t) * (((e_asin_pS0) + (((t) * (((e_asin_pS1) + (((t) * (((e_asin_pS2) + (((t) * (((e_asin_pS3) + (((t) * (((e_asin_pS4) + (((t) * (e_asin_pS5))))))))))))))))))))));
                    q = ((e_asin_one) + (((t) * (((e_asin_qS1) + (((t) * (((e_asin_qS2) + (((t) * (((e_asin_qS3) + (((t) * (e_asin_qS4))))))))))))))));
                    w = ((p) / (q));
                    return ((x) + (((x) * (w))));
                }
            }
            w = ((e_asin_one) - (Math.Abs(x)));
            t = ((w) * (0.5));
            p = ((t) * (((e_asin_pS0) + (((t) * (((e_asin_pS1) + (((t) * (((e_asin_pS2) + (((t) * (((e_asin_pS3) + (((t) * (((e_asin_pS4) + (((t) * (e_asin_pS5))))))))))))))))))))));
            q = ((e_asin_one) + (((t) * (((e_asin_qS1) + (((t) * (((e_asin_qS2) + (((t) * (((e_asin_qS3) + (((t) * (e_asin_qS4))))))))))))))));
            s = Math.Sqrt(t);
            if (((ix) >= (0x3FEF3333)))
            {
                w = ((p) / (q));
                t = ((e_asin_pio2_hi) - (((((2.0) * (((s) + (((s) * (w))))))) - (e_asin_pio2_lo))));
            }
            else
            {
                w = s;
                w = withLowWord(w, 0);
                c = ((((t) - (((w) * (w))))) / (((s) + (w))));
                r = ((p) / (q));
                p = ((((((2.0) * (s))) * (r))) - (((e_asin_pio2_lo) - (((2.0) * (c))))));
                q = ((e_asin_pio4_hi) - (((2.0) * (w))));
                t = ((e_asin_pio4_hi) - (((p) - (q))));
            }
            if (((hx) > (0)))
            {
                return t;
            }
            else
            {
                return (-t);
            }
        }
        // e_acos.c
        private const double e_acos_one = 1.00000000000000000000e+00;
        private const double e_acos_pi = 3.14159265358979311600e+00;
        private const double e_acos_pio2_hi = 1.57079632679489655800e+00;
        private const double e_acos_pio2_lo = 6.12323399573676603587e-17;
        private const double e_acos_pS0 = 1.66666666666666657415e-01;
        private const double e_acos_pS1 = (-3.25565818622400915405e-01);
        private const double e_acos_pS2 = 2.01212532134862925881e-01;
        private const double e_acos_pS3 = (-4.00555345006794114027e-02);
        private const double e_acos_pS4 = 7.91534994289814532176e-04;
        private const double e_acos_pS5 = 3.47933107596021167570e-05;
        private const double e_acos_qS1 = (-2.40339491173441421878e+00);
        private const double e_acos_qS2 = 2.02094576023350569471e+00;
        private const double e_acos_qS3 = (-6.88283971605453293030e-01);
        private const double e_acos_qS4 = 7.70381505559019352791e-02;
        private static double kernel___ieee754_acos(double x)
        {
            double z = 0;
            double p = 0;
            double q = 0;
            double r = 0;
            double w = 0;
            double s = 0;
            double c = 0;
            double df = 0;
            int hx = 0;
            int ix = 0;
            hx = highWord(x);
            ix = ((hx) & (0x7fffffff));
            if (((ix) >= (0x3ff00000)))
            {
                if (((((((ix) - (0x3ff00000))) | (lowWord(x)))) == (0)))
                {
                    if (((hx) > (0)))
                    {
                        return 0.0;
                    }
                    else
                    {
                        return ((e_acos_pi) + (((2.0) * (e_acos_pio2_lo))));
                    }
                }
                return ((((x) - (x))) / (((x) - (x))));
            }
            if (((ix) < (0x3fe00000)))
            {
                if (((ix) <= (0x3c600000)))
                {
                    return ((e_acos_pio2_hi) + (e_acos_pio2_lo));
                }
                z = ((x) * (x));
                p = ((z) * (((e_acos_pS0) + (((z) * (((e_acos_pS1) + (((z) * (((e_acos_pS2) + (((z) * (((e_acos_pS3) + (((z) * (((e_acos_pS4) + (((z) * (e_acos_pS5))))))))))))))))))))));
                q = ((e_acos_one) + (((z) * (((e_acos_qS1) + (((z) * (((e_acos_qS2) + (((z) * (((e_acos_qS3) + (((z) * (e_acos_qS4))))))))))))))));
                r = ((p) / (q));
                return ((e_acos_pio2_hi) - (((x) - (((e_acos_pio2_lo) - (((x) * (r))))))));
            }
            else
            {
                if (((hx) < (0)))
                {
                    z = ((((e_acos_one) + (x))) * (0.5));
                    p = ((z) * (((e_acos_pS0) + (((z) * (((e_acos_pS1) + (((z) * (((e_acos_pS2) + (((z) * (((e_acos_pS3) + (((z) * (((e_acos_pS4) + (((z) * (e_acos_pS5))))))))))))))))))))));
                    q = ((e_acos_one) + (((z) * (((e_acos_qS1) + (((z) * (((e_acos_qS2) + (((z) * (((e_acos_qS3) + (((z) * (e_acos_qS4))))))))))))))));
                    s = Math.Sqrt(z);
                    r = ((p) / (q));
                    w = ((((r) * (s))) - (e_acos_pio2_lo));
                    return ((e_acos_pi) - (((2.0) * (((s) + (w))))));
                }
                else
                {
                    z = ((((e_acos_one) - (x))) * (0.5));
                    s = Math.Sqrt(z);
                    df = s;
                    df = withLowWord(df, 0);
                    c = ((((z) - (((df) * (df))))) / (((s) + (df))));
                    p = ((z) * (((e_acos_pS0) + (((z) * (((e_acos_pS1) + (((z) * (((e_acos_pS2) + (((z) * (((e_acos_pS3) + (((z) * (((e_acos_pS4) + (((z) * (e_acos_pS5))))))))))))))))))))));
                    q = ((e_acos_one) + (((z) * (((e_acos_qS1) + (((z) * (((e_acos_qS2) + (((z) * (((e_acos_qS3) + (((z) * (e_acos_qS4))))))))))))))));
                    r = ((p) / (q));
                    w = ((((r) * (s))) + (c));
                    return ((2.0) * (((df) + (w))));
                }
            }
        }
        // s_atan.c
        private static readonly double[] s_atan_atanhi = { 4.63647609000806093515e-01, 7.85398163397448278999e-01, 9.82793723247329054082e-01, 1.57079632679489655800e+00 };
        private static readonly double[] s_atan_atanlo = { 2.26987774529616870924e-17, 3.06161699786838301793e-17, 1.39033110312309984516e-17, 6.12323399573676603587e-17 };
        private static readonly double[] s_atan_aT = { 3.33333333333329318027e-01, (-1.99999999998764832476e-01), 1.42857142725034663711e-01, (-1.11111104054623557880e-01), 9.09088713343650656196e-02, (-7.69187620504482999495e-02), 6.66107313738753120669e-02, (-5.83357013379057348645e-02), 4.97687799461593236017e-02, (-3.65315727442169155270e-02), 1.62858201153657823623e-02 };
        private const double s_atan_one = 1.0;
        private const double s_atan_huge = 1.0e300;
        private static double kernel_atan(double x)
        {
            double w = 0;
            double s1 = 0;
            double s2 = 0;
            double z = 0;
            int ix = 0;
            int hx = 0;
            int id = 0;
            hx = highWord(x);
            ix = ((hx) & (0x7fffffff));
            if (((ix) >= (0x44100000)))
            {
                if ((((ix) > (0x7ff00000)) || (((ix) == (0x7ff00000)) && ((lowWord(x)) != (0)))))
                {
                    return ((x) + (x));
                }
                if (((hx) > (0)))
                {
                    return ((s_atan_atanhi[3]) + (s_atan_atanlo[3]));
                }
                else
                {
                    return (((-s_atan_atanhi[3])) - (s_atan_atanlo[3]));
                }
            }
            if (((ix) < (0x3fdc0000)))
            {
                if (((ix) < (0x3e200000)))
                {
                    if (((((s_atan_huge) + (x))) > (s_atan_one)))
                    {
                        return x;
                    }
                }
                id = (-1);
            }
            else
            {
                x = Math.Abs(x);
                if (((ix) < (0x3ff30000)))
                {
                    if (((ix) < (0x3fe60000)))
                    {
                        id = 0;
                        x = ((((((2.0) * (x))) - (s_atan_one))) / (((2.0) + (x))));
                    }
                    else
                    {
                        id = 1;
                        x = ((((x) - (s_atan_one))) / (((x) + (s_atan_one))));
                    }
                }
                else
                {
                    if (((ix) < (0x40038000)))
                    {
                        id = 2;
                        x = ((((x) - (1.5))) / (((s_atan_one) + (((1.5) * (x))))));
                    }
                    else
                    {
                        id = 3;
                        x = (((-1.0)) / (x));
                    }
                }
            }
            z = ((x) * (x));
            w = ((z) * (z));
            s1 = ((z) * (((s_atan_aT[0]) + (((w) * (((s_atan_aT[2]) + (((w) * (((s_atan_aT[4]) + (((w) * (((s_atan_aT[6]) + (((w) * (((s_atan_aT[8]) + (((w) * (s_atan_aT[10]))))))))))))))))))))));
            s2 = ((w) * (((s_atan_aT[1]) + (((w) * (((s_atan_aT[3]) + (((w) * (((s_atan_aT[5]) + (((w) * (((s_atan_aT[7]) + (((w) * (s_atan_aT[9]))))))))))))))))));
            if (((id) < (0)))
            {
                return ((x) - (((x) * (((s1) + (s2))))));
            }
            else
            {
                z = ((s_atan_atanhi[id]) - (((((((x) * (((s1) + (s2))))) - (s_atan_atanlo[id]))) - (x))));
                return (((hx) < (0)) ? (-z) : z);
            }
        }
        // e_atan2.c
        private const double e_atan2_tiny = 1.0e-300;
        private const double e_atan2_zero = 0.0;
        private const double e_atan2_pi_o_4 = 7.8539816339744827900E-01;
        private const double e_atan2_pi_o_2 = 1.5707963267948965580E+00;
        private const double e_atan2_pi = 3.1415926535897931160E+00;
        private const double e_atan2_pi_lo = 1.2246467991473531772E-16;
        private static double kernel___ieee754_atan2(double y, double x)
        {
            double z = 0;
            int k = 0;
            int m = 0;
            int hx = 0;
            int hy = 0;
            int ix = 0;
            int iy = 0;
            uint lx = 0;
            uint ly = 0;
            hx = highWord(x);
            ix = ((hx) & (0x7fffffff));
            lx = unchecked((uint)(lowWord(x)));
            hy = highWord(y);
            iy = ((hy) & (0x7fffffff));
            ly = unchecked((uint)(lowWord(y)));
            if (((((unchecked((uint)((ix))) | (((((lx) | ((-lx)))) >> (31))))) > unchecked((uint)((0x7ff00000)))) || (((unchecked((uint)((iy))) | (((((ly) | ((-ly)))) >> (31))))) > unchecked((uint)((0x7ff00000))))))
            {
                return ((x) + (y));
            }
            if ((((unchecked((uint)((((hx) - (0x3ff00000))))) | (lx))) == unchecked((uint)((0)))))
            {
                return kernel_atan(y);
            }
            m = ((((((hy) >> (31))) & (1))) | (((((hx) >> (30))) & (2))));
            if ((((unchecked((uint)((iy))) | (ly))) == unchecked((uint)((0)))))
            {
                switch (m)
                {
                    case 0:

                    case 1:
                        return y;
                    case 2:
                        return ((e_atan2_pi) + (e_atan2_tiny));
                    case 3:
                        return (((-e_atan2_pi)) - (e_atan2_tiny));
                }
            }
            if ((((unchecked((uint)((ix))) | (lx))) == unchecked((uint)((0)))))
            {
                return (((hy) < (0)) ? (((-e_atan2_pi_o_2)) - (e_atan2_tiny)) : ((e_atan2_pi_o_2) + (e_atan2_tiny)));
            }
            if (((ix) == (0x7ff00000)))
            {
                if (((iy) == (0x7ff00000)))
                {
                    switch (m)
                    {
                        case 0:
                            return ((e_atan2_pi_o_4) + (e_atan2_tiny));
                        case 1:
                            return (((-e_atan2_pi_o_4)) - (e_atan2_tiny));
                        case 2:
                            return ((((3.0) * (e_atan2_pi_o_4))) + (e_atan2_tiny));
                        case 3:
                            return (((((-3.0)) * (e_atan2_pi_o_4))) - (e_atan2_tiny));
                    }
                }
                else
                {
                    switch (m)
                    {
                        case 0:
                            return e_atan2_zero;
                        case 1:
                            return (-e_atan2_zero);
                        case 2:
                            return ((e_atan2_pi) + (e_atan2_tiny));
                        case 3:
                            return (((-e_atan2_pi)) - (e_atan2_tiny));
                    }
                }
            }
            if (((iy) == (0x7ff00000)))
            {
                return (((hy) < (0)) ? (((-e_atan2_pi_o_2)) - (e_atan2_tiny)) : ((e_atan2_pi_o_2) + (e_atan2_tiny)));
            }
            k = ((((iy) - (ix))) >> (20));
            if (((k) > (60)))
            {
                z = ((e_atan2_pi_o_2) + (((0.5) * (e_atan2_pi_lo))));
            }
            else
            {
                if ((((hx) < (0)) && ((k) < ((-60)))))
                {
                    z = 0.0;
                }
                else
                {
                    z = kernel_atan(Math.Abs(((y) / (x))));
                }
            }
            switch (m)
            {
                case 0:
                    return z;
                case 1:
                    z = withHighWord(z, unchecked((int)((highWord(z) ^ 0x80000000))));
                    return z;
                case 2:
                    return ((e_atan2_pi) - (((z) - (e_atan2_pi_lo))));
                default:
                    return ((((z) - (e_atan2_pi_lo))) - (e_atan2_pi));
            }
        }
        // e_rem_pio2.c
        private static readonly int[] e_rem_pio2_two_over_pi = { 0xA2F983, 0x6E4E44, 0x1529FC, 0x2757D1, 0xF534DD, 0xC0DB62, 0x95993C, 0x439041, 0xFE5163, 0xABDEBB, 0xC561B7, 0x246E3A, 0x424DD2, 0xE00649, 0x2EEA09, 0xD1921C, 0xFE1DEB, 0x1CB129, 0xA73EE8, 0x8235F5, 0x2EBB44, 0x84E99C, 0x7026B4, 0x5F7E41, 0x3991D6, 0x398353, 0x39F49C, 0x845F8B, 0xBDF928, 0x3B1FF8, 0x97FFDE, 0x05980F, 0xEF2F11, 0x8B5A0A, 0x6D1F6D, 0x367ECF, 0x27CB09, 0xB74F46, 0x3F669E, 0x5FEA2D, 0x7527BA, 0xC7EBE5, 0xF17B3D, 0x0739F7, 0x8A5292, 0xEA6BFB, 0x5FB11F, 0x8D5D08, 0x560330, 0x46FC7B, 0x6BABF0, 0xCFBC20, 0x9AF436, 0x1DA9E3, 0x91615E, 0xE61B08, 0x659985, 0x5F14A0, 0x68408D, 0xFFD880, 0x4D7327, 0x310606, 0x1556CA, 0x73A8C9, 0x60E27B, 0xC08C6B };
        private static readonly int[] e_rem_pio2_npio2_hw = { 0x3FF921FB, 0x400921FB, 0x4012D97C, 0x401921FB, 0x401F6A7A, 0x4022D97C, 0x4025FDBB, 0x402921FB, 0x402C463A, 0x402F6A7A, 0x4031475C, 0x4032D97C, 0x40346B9C, 0x4035FDBB, 0x40378FDB, 0x403921FB, 0x403AB41B, 0x403C463A, 0x403DD85A, 0x403F6A7A, 0x40407E4C, 0x4041475C, 0x4042106C, 0x4042D97C, 0x4043A28C, 0x40446B9C, 0x404534AC, 0x4045FDBB, 0x4046C6CB, 0x40478FDB, 0x404858EB, 0x404921FB };
        private const double e_rem_pio2_zero = 0.00000000000000000000e+00;
        private const double e_rem_pio2_half = 5.00000000000000000000e-01;
        private const double e_rem_pio2_two24 = 1.67772160000000000000e+07;
        private const double e_rem_pio2_invpio2 = 6.36619772367581382433e-01;
        private const double e_rem_pio2_pio2_1 = 1.57079632673412561417e+00;
        private const double e_rem_pio2_pio2_1t = 6.07710050650619224932e-11;
        private const double e_rem_pio2_pio2_2 = 6.07710050630396597660e-11;
        private const double e_rem_pio2_pio2_2t = 2.02226624879595063154e-21;
        private const double e_rem_pio2_pio2_3 = 2.02226624871116645580e-21;
        private const double e_rem_pio2_pio2_3t = 8.47842766036889956997e-32;
        private static int kernel___ieee754_rem_pio2(double x, double[] y)
        {
            double z = 0;
            double w = 0;
            double t = 0;
            double r = 0;
            double fn = 0;
            double[] tx = (e_rem_pio2_tx ??= new double[3]);
            int e0 = 0;
            int i = 0;
            int j = 0;
            int nx = 0;
            int n = 0;
            int ix = 0;
            int hx = 0;
            hx = highWord(x);
            ix = ((hx) & (0x7fffffff));
            if (((ix) <= (0x3fe921fb)))
            {
                y[0] = x;
                y[1] = 0;
                return 0;
            }
            if (((ix) < (0x4002d97c)))
            {
                if (((hx) > (0)))
                {
                    z = ((x) - (e_rem_pio2_pio2_1));
                    if (((ix) != (0x3ff921fb)))
                    {
                        y[0] = ((z) - (e_rem_pio2_pio2_1t));
                        y[1] = ((((z) - (y[0]))) - (e_rem_pio2_pio2_1t));
                    }
                    else
                    {
                        z = (z - e_rem_pio2_pio2_2);
                        y[0] = ((z) - (e_rem_pio2_pio2_2t));
                        y[1] = ((((z) - (y[0]))) - (e_rem_pio2_pio2_2t));
                    }
                    return 1;
                }
                else
                {
                    z = ((x) + (e_rem_pio2_pio2_1));
                    if (((ix) != (0x3ff921fb)))
                    {
                        y[0] = ((z) + (e_rem_pio2_pio2_1t));
                        y[1] = ((((z) - (y[0]))) + (e_rem_pio2_pio2_1t));
                    }
                    else
                    {
                        z = (z + e_rem_pio2_pio2_2);
                        y[0] = ((z) + (e_rem_pio2_pio2_2t));
                        y[1] = ((((z) - (y[0]))) + (e_rem_pio2_pio2_2t));
                    }
                    return (-1);
                }
            }
            if (((ix) <= (0x413921fb)))
            {
                t = Math.Abs(x);
                n = unchecked((int)(((((t) * (e_rem_pio2_invpio2))) + (e_rem_pio2_half))));
                fn = ((double)(n));
                r = ((t) - (((fn) * (e_rem_pio2_pio2_1))));
                w = ((fn) * (e_rem_pio2_pio2_1t));
                if ((((n) < (32)) && ((ix) != (e_rem_pio2_npio2_hw[((n) - (1))]))))
                {
                    y[0] = ((r) - (w));
                }
                else
                {
                    j = ((ix) >> (20));
                    y[0] = ((r) - (w));
                    i = ((j) - (((((highWord(y[0])) >> (20))) & (0x7ff))));
                    if (((i) > (16)))
                    {
                        t = r;
                        w = ((fn) * (e_rem_pio2_pio2_2));
                        r = ((t) - (w));
                        w = ((((fn) * (e_rem_pio2_pio2_2t))) - (((((t) - (r))) - (w))));
                        y[0] = ((r) - (w));
                        i = ((j) - (((((highWord(y[0])) >> (20))) & (0x7ff))));
                        if (((i) > (49)))
                        {
                            t = r;
                            w = ((fn) * (e_rem_pio2_pio2_3));
                            r = ((t) - (w));
                            w = ((((fn) * (e_rem_pio2_pio2_3t))) - (((((t) - (r))) - (w))));
                            y[0] = ((r) - (w));
                        }
                    }
                }
                y[1] = ((((r) - (y[0]))) - (w));
                if (((hx) < (0)))
                {
                    y[0] = (-y[0]);
                    y[1] = (-y[1]);
                    return (-n);
                }
                else
                {
                    return n;
                }
            }
            if (((ix) >= (0x7ff00000)))
            {
                y[0] = y[1] = ((x) - (x));
                return 0;
            }
            z = withLowWord(z, lowWord(x));
            e0 = ((((ix) >> (20))) - (1046));
            z = withHighWord(z, ((ix) - (((e0) << (20)))));
            for (i = 0; ((i) < (2)); i++)
            {
                tx[i] = ((double)(unchecked((int)(z))));
                z = ((((z) - (tx[i]))) * (e_rem_pio2_two24));
            }
            tx[2] = z;
            nx = 3;
            while (((tx[((nx) - (1))]) == (e_rem_pio2_zero)))
            {
                nx--;
            }
            n = kernel___kernel_rem_pio2(tx, y, e0, nx, 2, e_rem_pio2_two_over_pi);
            if (((hx) < (0)))
            {
                y[0] = (-y[0]);
                y[1] = (-y[1]);
                return (-n);
            }
            return n;
        }
        // k_rem_pio2.c
        private static readonly int[] k_rem_pio2_init_jk = { 2, 3, 4, 6 };
        private static readonly double[] k_rem_pio2_PIo2 = { 1.57079625129699707031e+00, 7.54978941586159635335e-08, 5.39030252995776476554e-15, 3.28200341580791294123e-22, 1.27065575308067607349e-29, 1.22933308981111328932e-36, 2.73370053816464559624e-44, 2.16741683877804819444e-51 };
        private const double k_rem_pio2_zero = 0.0;
        private const double k_rem_pio2_one = 1.0;
        private const double k_rem_pio2_two24 = 1.67772160000000000000e+07;
        private const double k_rem_pio2_twon24 = 5.96046447753906250000e-08;
        private static int kernel___kernel_rem_pio2(double[] x, double[] y, int e0, int nx, int prec, int[] ipio2)
        {
            int jz = 0;
            int jx = 0;
            int jv = 0;
            int jp = 0;
            int jk = 0;
            int carry = 0;
            int n = 0;
            int[] iq = (k_rem_pio2_iq ??= new int[20]);
            int i = 0;
            int j = 0;
            int k = 0;
            int m = 0;
            int q0 = 0;
            int ih = 0;
            double z = 0;
            double fw = 0;
            double[] f = (k_rem_pio2_f ??= new double[20]);
            double[] fq = (k_rem_pio2_fq ??= new double[20]);
            double[] q = (k_rem_pio2_q ??= new double[20]);
            jk = k_rem_pio2_init_jk[prec];
            jp = jk;
            jx = ((nx) - (1));
            jv = ((((e0) - (3))) / (24));
            if (((jv) < (0)))
            {
                jv = 0;
            }
            q0 = ((e0) - (((24) * (((jv) + (1))))));
            j = ((jv) - (jx));
            m = ((jx) + (jk));
            for (i = 0; ((i) <= (m)); i++, j++)
            {
                f[i] = (((j) < (0)) ? k_rem_pio2_zero : ((double)(ipio2[j])));
            }
            for (i = 0; ((i) <= (jk)); i++)
            {
                for (j = 0, fw = 0.0; ((j) <= (jx)); j++)
                {
                    fw = (fw + ((x[j]) * (f[((((jx) + (i))) - (j))])));
                }
                q[i] = fw;
            }
            jz = jk;
            while (true)
            {
                for (i = 0, j = jz, z = q[jz]; ((j) > (0)); i++, j--)
                {
                    fw = ((double)(unchecked((int)(((k_rem_pio2_twon24) * (z))))));
                    iq[i] = unchecked((int)(((z) - (((k_rem_pio2_two24) * (fw))))));
                    z = ((q[((j) - (1))]) + (fw));
                }
                z = kernel_scalbn(z, q0);
                z = (z - ((8.0) * (Math.Floor(((z) * (0.125))))));
                n = unchecked((int)(z));
                z = (z - ((double)(n)));
                ih = 0;
                if (((q0) > (0)))
                {
                    i = ((iq[((jz) - (1))]) >> (((24) - (q0))));
                    n = (n + i);
                    iq[((jz) - (1))] = (iq[((jz) - (1))] - ((i) << (((24) - (q0)))));
                    ih = ((iq[((jz) - (1))]) >> (((23) - (q0))));
                }
                else
                {
                    if (((q0) == (0)))
                    {
                        ih = ((iq[((jz) - (1))]) >> (23));
                    }
                    else
                    {
                        if (((z) >= (0.5)))
                        {
                            ih = 2;
                        }
                    }
                }
                if (((ih) > (0)))
                {
                    n = (n + 1);
                    carry = 0;
                    for (i = 0; ((i) < (jz)); i++)
                    {
                        j = iq[i];
                        if (((carry) == (0)))
                        {
                            if (((j) != (0)))
                            {
                                carry = 1;
                                iq[i] = ((0x1000000) - (j));
                            }
                        }
                        else
                        {
                            iq[i] = ((0xffffff) - (j));
                        }
                    }
                    if (((q0) > (0)))
                    {
                        switch (q0)
                        {
                            case 1:
                                iq[((jz) - (1))] = (iq[((jz) - (1))] & 0x7fffff);
                                break;
                            case 2:
                                iq[((jz) - (1))] = (iq[((jz) - (1))] & 0x3fffff);
                                break;
                        }
                    }
                    if (((ih) == (2)))
                    {
                        z = ((k_rem_pio2_one) - (z));
                        if (((carry) != (0)))
                        {
                            z = (z - kernel_scalbn(k_rem_pio2_one, q0));
                        }
                    }
                }
                if (((z) == (k_rem_pio2_zero)))
                {
                    j = 0;
                    for (i = ((jz) - (1)); ((i) >= (jk)); i--)
                    {
                        j = (j | iq[i]);
                    }
                    if (((j) == (0)))
                    {
                        for (k = 1; ((iq[((jk) - (k))]) == (0)); k++)
                        {
                            ;
                        }
                        for (i = ((jz) + (1)); ((i) <= (((jz) + (k)))); i++)
                        {
                            f[((jx) + (i))] = ((double)(ipio2[((jv) + (i))]));
                            for (j = 0, fw = 0.0; ((j) <= (jx)); j++)
                            {
                                fw = (fw + ((x[j]) * (f[((((jx) + (i))) - (j))])));
                            }
                            q[i] = fw;
                        }
                        jz = (jz + k);
                        continue;
                    }
                }
                break;
            }
            if (((z) == (0.0)))
            {
                jz = (jz - 1);
                q0 = (q0 - 24);
                while (((iq[jz]) == (0)))
                {
                    jz--;
                    q0 = (q0 - 24);
                }
            }
            else
            {
                z = kernel_scalbn(z, (-q0));
                if (((z) >= (k_rem_pio2_two24)))
                {
                    fw = ((double)(unchecked((int)(((k_rem_pio2_twon24) * (z))))));
                    iq[jz] = unchecked((int)(((z) - (((k_rem_pio2_two24) * (fw))))));
                    jz = (jz + 1);
                    q0 = (q0 + 24);
                    iq[jz] = unchecked((int)(fw));
                }
                else
                {
                    iq[jz] = unchecked((int)(z));
                }
            }
            fw = kernel_scalbn(k_rem_pio2_one, q0);
            for (i = jz; ((i) >= (0)); i--)
            {
                q[i] = ((fw) * (((double)(iq[i]))));
                fw = (fw * k_rem_pio2_twon24);
            }
            for (i = jz; ((i) >= (0)); i--)
            {
                for (fw = 0.0, k = 0; (((k) <= (jp)) && ((k) <= (((jz) - (i))))); k++)
                {
                    fw = (fw + ((k_rem_pio2_PIo2[k]) * (q[((i) + (k))])));
                }
                fq[((jz) - (i))] = fw;
            }
            switch (prec)
            {
                case 0:
                    fw = 0.0;
                    for (i = jz; ((i) >= (0)); i--)
                    {
                        fw = (fw + fq[i]);
                    }
                    y[0] = (((ih) == (0)) ? fw : (-fw));
                    break;
                case 1:

                case 2:
                    fw = 0.0;
                    for (i = jz; ((i) >= (0)); i--)
                    {
                        fw = (fw + fq[i]);
                    }
                    y[0] = (((ih) == (0)) ? fw : (-fw));
                    fw = ((fq[0]) - (fw));
                    for (i = 1; ((i) <= (jz)); i++)
                    {
                        fw = (fw + fq[i]);
                    }
                    y[1] = (((ih) == (0)) ? fw : (-fw));
                    break;
                case 3:
                    for (i = jz; ((i) > (0)); i--)
                    {
                        fw = ((fq[((i) - (1))]) + (fq[i]));
                        fq[i] = (fq[i] + ((fq[((i) - (1))]) - (fw)));
                        fq[((i) - (1))] = fw;
                    }
                    for (i = jz; ((i) > (1)); i--)
                    {
                        fw = ((fq[((i) - (1))]) + (fq[i]));
                        fq[i] = (fq[i] + ((fq[((i) - (1))]) - (fw)));
                        fq[((i) - (1))] = fw;
                    }
                    for (fw = 0.0, i = jz; ((i) >= (2)); i--)
                    {
                        fw = (fw + fq[i]);
                    }
                    if (((ih) == (0)))
                    {
                        y[0] = fq[0];
                        y[1] = fq[1];
                        y[2] = fw;
                    }
                    else
                    {
                        y[0] = (-fq[0]);
                        y[1] = (-fq[1]);
                        y[2] = (-fw);
                    }
                    break;
            }
            return ((n) & (7));
        }
        // k_sin.c
        private const double k_sin_half = 5.00000000000000000000e-01;
        private const double k_sin_S1 = (-1.66666666666666324348e-01);
        private const double k_sin_S2 = 8.33333333332248946124e-03;
        private const double k_sin_S3 = (-1.98412698298579493134e-04);
        private const double k_sin_S4 = 2.75573137070700676789e-06;
        private const double k_sin_S5 = (-2.50507602534068634195e-08);
        private const double k_sin_S6 = 1.58969099521155010221e-10;
        private static double kernel___kernel_sin(double x, double y, int iy)
        {
            double z = 0;
            double r = 0;
            double v = 0;
            int ix = 0;
            ix = ((highWord(x)) & (0x7fffffff));
            if (((ix) < (0x3e400000)))
            {
                if (((unchecked((int)(x))) == (0)))
                {
                    return x;
                }
            }
            z = ((x) * (x));
            v = ((z) * (x));
            r = ((k_sin_S2) + (((z) * (((k_sin_S3) + (((z) * (((k_sin_S4) + (((z) * (((k_sin_S5) + (((z) * (k_sin_S6))))))))))))))));
            if (((iy) == (0)))
            {
                return ((x) + (((v) * (((k_sin_S1) + (((z) * (r))))))));
            }
            else
            {
                return ((x) - (((((((z) * (((((k_sin_half) * (y))) - (((v) * (r))))))) - (y))) - (((v) * (k_sin_S1))))));
            }
        }
        // k_cos.c
        private const double k_cos_one = 1.00000000000000000000e+00;
        private const double k_cos_C1 = 4.16666666666666019037e-02;
        private const double k_cos_C2 = (-1.38888888888741095749e-03);
        private const double k_cos_C3 = 2.48015872894767294178e-05;
        private const double k_cos_C4 = (-2.75573143513906633035e-07);
        private const double k_cos_C5 = 2.08757232129817482790e-09;
        private const double k_cos_C6 = (-1.13596475577881948265e-11);
        private static double kernel___kernel_cos(double x, double y)
        {
            double a = 0;
            double hz = 0;
            double z = 0;
            double r = 0;
            double qx = 0;
            int ix = 0;
            ix = ((highWord(x)) & (0x7fffffff));
            if (((ix) < (0x3e400000)))
            {
                if (((unchecked((int)(x))) == (0)))
                {
                    return k_cos_one;
                }
            }
            z = ((x) * (x));
            r = ((z) * (((k_cos_C1) + (((z) * (((k_cos_C2) + (((z) * (((k_cos_C3) + (((z) * (((k_cos_C4) + (((z) * (((k_cos_C5) + (((z) * (k_cos_C6))))))))))))))))))))));
            if (((ix) < (0x3FD33333)))
            {
                return ((k_cos_one) - (((((0.5) * (z))) - (((((z) * (r))) - (((x) * (y))))))));
            }
            else
            {
                if (((ix) > (0x3fe90000)))
                {
                    qx = 0.28125;
                }
                else
                {
                    qx = withHighWord(qx, ((ix) - (0x00200000)));
                    qx = withLowWord(qx, 0);
                }
                hz = ((((0.5) * (z))) - (qx));
                a = ((k_cos_one) - (qx));
                return ((a) - (((hz) - (((((z) * (r))) - (((x) * (y))))))));
            }
        }
        // k_tan.c
        private static readonly double[] k_tan_xxx = { 3.33333333333334091986e-01, 1.33333333333201242699e-01, 5.39682539762260521377e-02, 2.18694882948595424599e-02, 8.86323982359930005737e-03, 3.59207910759131235356e-03, 1.45620945432529025516e-03, 5.88041240820264096874e-04, 2.46463134818469906812e-04, 7.81794442939557092300e-05, 7.14072491382608190305e-05, (-1.85586374855275456654e-05), 2.59073051863633712884e-05, 1.00000000000000000000e+00, 7.85398163397448278999e-01, 3.06161699786838301793e-17 };
        private static double kernel___kernel_tan(double x, double y, int iy)
        {
            double z = 0;
            double r = 0;
            double v = 0;
            double w = 0;
            double s = 0;
            int ix = 0;
            int hx = 0;
            hx = highWord(x);
            ix = ((hx) & (0x7fffffff));
            if (((ix) < (0x3e300000)))
            {
                if (((unchecked((int)(x))) == (0)))
                {
                    if (((((((ix) | (lowWord(x)))) | (((iy) + (1))))) == (0)))
                    {
                        return ((k_tan_xxx[13]) / (Math.Abs(x)));
                    }
                    else
                    {
                        if (((iy) == (1)))
                        {
                            return x;
                        }
                        else
                        {
                            double a = 0;
                            double t = 0;
                            z = w = ((x) + (y));
                            z = withLowWord(z, 0);
                            v = ((y) - (((z) - (x))));
                            t = a = (((-k_tan_xxx[13])) / (w));
                            t = withLowWord(t, 0);
                            s = ((k_tan_xxx[13]) + (((t) * (z))));
                            return ((t) + (((a) * (((s) + (((t) * (v))))))));
                        }
                    }
                }
            }
            if (((ix) >= (0x3FE59428)))
            {
                if (((hx) < (0)))
                {
                    x = (-x);
                    y = (-y);
                }
                z = ((k_tan_xxx[14]) - (x));
                w = ((k_tan_xxx[15]) - (y));
                x = ((z) + (w));
                y = 0.0;
            }
            z = ((x) * (x));
            w = ((z) * (z));
            r = ((k_tan_xxx[1]) + (((w) * (((k_tan_xxx[3]) + (((w) * (((k_tan_xxx[5]) + (((w) * (((k_tan_xxx[7]) + (((w) * (((k_tan_xxx[9]) + (((w) * (k_tan_xxx[11]))))))))))))))))))));
            v = ((z) * (((k_tan_xxx[2]) + (((w) * (((k_tan_xxx[4]) + (((w) * (((k_tan_xxx[6]) + (((w) * (((k_tan_xxx[8]) + (((w) * (((k_tan_xxx[10]) + (((w) * (k_tan_xxx[12]))))))))))))))))))))));
            s = ((z) * (x));
            r = ((y) + (((z) * (((((s) * (((r) + (v))))) + (y))))));
            r = (r + ((k_tan_xxx[0]) * (s)));
            w = ((x) + (r));
            if (((ix) >= (0x3FE59428)))
            {
                v = ((double)(iy));
                return ((((double)(((1) - (((((hx) >> (30))) & (2))))))) * (((v) - (((2.0) * (((x) - (((((((w) * (w))) / (((w) + (v))))) - (r))))))))));
            }
            if (((iy) == (1)))
            {
                return w;
            }
            else
            {
                double a = 0;
                double t = 0;
                z = w;
                z = withLowWord(z, 0);
                v = ((r) - (((z) - (x))));
                t = a = (((-1.0)) / (w));
                t = withLowWord(t, 0);
                s = ((1.0) + (((t) * (z))));
                return ((t) + (((a) * (((s) + (((t) * (v))))))));
            }
        }
        // s_scalbn.c
        private const double s_scalbn_two54 = 1.80143985094819840000e+16;
        private const double s_scalbn_twom54 = 5.55111512312578270212e-17;
        private const double s_scalbn_huge = 1.0e+300;
        private const double s_scalbn_tiny = 1.0e-300;
        private static double kernel_scalbn(double x, int n)
        {
            int k = 0;
            int hx = 0;
            int lx = 0;
            hx = highWord(x);
            lx = lowWord(x);
            k = ((((hx) & (0x7ff00000))) >> (20));
            if (((k) == (0)))
            {
                if (((((lx) | (((hx) & (0x7fffffff))))) == (0)))
                {
                    return x;
                }
                x = (x * s_scalbn_two54);
                hx = highWord(x);
                k = ((((((hx) & (0x7ff00000))) >> (20))) - (54));
                if (((n) < ((-50000))))
                {
                    return ((s_scalbn_tiny) * (x));
                }
            }
            if (((k) == (0x7ff)))
            {
                return ((x) + (x));
            }
            k = ((k) + (n));
            if (((k) > (0x7fe)))
            {
                return ((s_scalbn_huge) * (copysign(s_scalbn_huge, x)));
            }
            if (((k) > (0)))
            {
                x = withHighWord(x, unchecked((int)((((unchecked((uint)((hx))) & (0x800fffff))) | unchecked((uint)((((k) << (20)))))))));
                return x;
            }
            if (((k) <= ((-54))))
            {
                if (((n) > (50000)))
                {
                    return ((s_scalbn_huge) * (copysign(s_scalbn_huge, x)));
                }
                else
                {
                    return ((s_scalbn_tiny) * (copysign(s_scalbn_tiny, x)));
                }
            }
            k = (k + 54);
            x = withHighWord(x, unchecked((int)((((unchecked((uint)((hx))) & (0x800fffff))) | unchecked((uint)((((k) << (20)))))))));
            return ((x) * (s_scalbn_twom54));
        }
        internal static double Pow(double x, double y) => (double)(float)((x == 1 || (x == -1 && double.IsInfinity(y))) ? 1 : kernel___ieee754_pow(x, y));
        internal static double Exp(double x) => (double)(float)kernel___ieee754_exp(x);
        internal static double Log(double x) => (double)(float)kernel___ieee754_log(x);
        internal static double Log10(double x) => (double)(float)kernel___ieee754_log10(x);
        internal static double Sin(double x) => (double)(float)kernel_sin(x);
        internal static double Cos(double x) => (double)(float)kernel_cos(x);
        internal static double Tan(double x) => (double)(float)kernel_tan(x);
        internal static double Asin(double x) => (double)(float)kernel___ieee754_asin(x);
        internal static double Acos(double x) => (double)(float)kernel___ieee754_acos(x);
        internal static double Atan(double x) => (double)(float)kernel_atan(x);
        internal static double Atan2(double x, double y) => (double)(float)kernel___ieee754_atan2(x, y);
    }
}