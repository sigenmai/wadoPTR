using System.Linq;
using NUnit.Framework;
using HorseRace;

namespace HorseRace.Tests
{
    public class SpeedModTests
    {
        [Test] public void SpeedMod_StacksWithFloor() { var f = new float[8]; var r = new float[8]; SpeedMod.Add(f, r, 0.8f, 1); SpeedMod.Add(f, r, 0.8f, 3); Assert.AreEqual(0.68f, SpeedMod.Combined(f, r), 1e-4); SpeedMod.Add(f, r, 0.5f, 2); Assert.AreEqual(0.55f, SpeedMod.Combined(f, r), 1e-4); }
        [Test] public void SpeedMod_ThreeHitsFloorAt055() { var f = new float[8]; var r = new float[8]; SpeedMod.Add(f, r, 0.6f, 1); Assert.AreEqual(0.6f, SpeedMod.Combined(f, r), 1e-4); SpeedMod.Add(f, r, 0.6f, 1); Assert.AreEqual(0.55f, SpeedMod.Combined(f, r), 1e-4); /* 0.6×0.85 = 0.51 → 下限 0.55 */ SpeedMod.Add(f, r, 0.6f, 1); Assert.AreEqual(0.55f, SpeedMod.Combined(f, r), 1e-4); }
        [Test] public void SpeedMod_BuffsMultiply() { var f = new float[8]; var r = new float[8]; SpeedMod.Add(f, r, 1.2f, 1); Assert.AreEqual(1.2f, SpeedMod.Combined(f, r), 1e-4); SpeedMod.Add(f, r, 0.8f, 1); Assert.AreEqual(0.96f, SpeedMod.Combined(f, r), 1e-4); }
        [Test] public void SpeedMod_Expires() { var f = new float[8]; var r = new float[8]; SpeedMod.Add(f, r, 0.6f, 1); SpeedMod.Tick(r, 1.01f); Assert.AreEqual(1f, SpeedMod.Combined(f, r), 1e-4); }
        [Test] public void SpeedMod_FullSlotsOverwriteShortest() { var f = new float[8]; var r = new float[8]; for (int i = 0; i < 8; i++) SpeedMod.Add(f, r, 0.99f, 10 + i); SpeedMod.Add(f, r, 0.5f, 5); Assert.AreEqual(5f, r.Min()); Assert.AreEqual(8, r.Count(x => x > 0)); }
    }
}
