using NUnit.Framework;
using UnityEngine;
using HorseRace;

namespace HorseRace.Tests
{
    public class InterpMathTests
    {
        private double[] t; private Vector3[] p; private float[] y; private float[] s;

        [SetUp] public void Init()
        {
            t = new double[InterpMath.BUFFER]; p = new Vector3[InterpMath.BUFFER];
            y = new float[InterpMath.BUFFER]; s = new float[InterpMath.BUFFER];
        }

        private int Add(int count, double time, float x) => InterpMath.Insert(t, p, y, s, count, time, new Vector3(x, 0, 0), x, x);

        [Test] public void InsertKeepsTimeOrderWhenPacketsArriveOutOfOrder()
        {
            int n = 0;
            n = Add(n, 1.0, 1); n = Add(n, 1.2, 3); n = Add(n, 1.1, 2);
            Assert.AreEqual(3, n);
            Assert.AreEqual(1.0, t[0]); Assert.AreEqual(1.1, t[1]); Assert.AreEqual(1.2, t[2]);
            Assert.AreEqual(2f, p[1].x);
        }

        [Test] public void InsertDropsOldestWhenFullAndIgnoresTooOld()
        {
            int n = 0;
            for (int i = 0; i < InterpMath.BUFFER; i++) n = Add(n, 1.0 + i * 0.1, i);
            n = Add(n, 2.0, 9);
            Assert.AreEqual(InterpMath.BUFFER, n);
            Assert.AreEqual(1.1, t[0], 1e-9);
            Assert.AreEqual(2.0, t[n - 1]);
            n = Add(n, 0.5, -1); // older than everything kept
            Assert.AreEqual(1.1, t[0], 1e-9);
        }

        [Test] public void InsertSameTimeReplaces()
        {
            int n = Add(0, 1.0, 1);
            n = Add(n, 1.0, 5);
            Assert.AreEqual(1, n);
            Assert.AreEqual(5f, p[0].x);
        }

        [Test] public void SpanStart()
        {
            int n = Add(0, 1.0, 0); n = Add(n, 1.1, 1); n = Add(n, 1.2, 2);
            Assert.AreEqual(-1, InterpMath.SpanStart(t, n, 0.9));
            Assert.AreEqual(0, InterpMath.SpanStart(t, n, 1.05));
            Assert.AreEqual(1, InterpMath.SpanStart(t, n, 1.1));
            Assert.AreEqual(2, InterpMath.SpanStart(t, n, 1.5)); // newest → extrapolate
            Assert.AreEqual(-1, InterpMath.SpanStart(t, 0, 1.0));
        }

        [Test] public void SpanT()
        {
            Assert.AreEqual(0.5f, InterpMath.SpanT(1.0, 1.2, 1.1), 1e-4f);
            Assert.AreEqual(1f, InterpMath.SpanT(1.0, 1.0, 1.0));
            Assert.AreEqual(1f, InterpMath.SpanT(1.0, 1.2, 2.0));
        }

        [Test] public void LerpAngleWraps()
        {
            Assert.AreEqual(0f, InterpMath.DeltaAngle(350f, 350f), 1e-4f);
            Assert.AreEqual(20f, InterpMath.DeltaAngle(350f, 10f), 1e-4f);
            Assert.AreEqual(-20f, InterpMath.DeltaAngle(10f, 350f), 1e-4f);
            Assert.AreEqual(360f, InterpMath.LerpAngle(350f, 10f, 0.5f), 1e-4f);
        }

        [Test] public void ClockAdvancesSmoothlyAndSnapsWhenFarOff()
        {
            double c = InterpMath.AdvanceClock(100.0, 0.016f, 100.016);
            Assert.AreEqual(100.016, c, 1e-6);
            c = InterpMath.AdvanceClock(100.0, 0.016f, 100.05); // small error: nudged, not jumped
            Assert.Greater(c, 100.016); Assert.Less(c, 100.05);
            Assert.AreEqual(105.0, InterpMath.AdvanceClock(100.0, 0.016f, 105.0), 1e-9);
        }

        [Test] public void DecayFactor()
        {
            Assert.AreEqual(1f, InterpMath.DecayFactor(0f, 0.15f), 1e-5f);
            Assert.Less(InterpMath.DecayFactor(0.15f, 0.15f), 0.4f);
            Assert.AreEqual(0f, InterpMath.DecayFactor(0.1f, 0f));
        }
    }
}
