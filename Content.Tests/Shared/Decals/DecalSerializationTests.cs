using System;
using System.Collections.Generic;
using Content.Shared.Decals;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared.Decals
{
    [TestFixture]
    public sealed class DecalSerializationTests
    {
        [Test]
        public void QuantizationRoundTripStaysWithinTolerance()
        {
            var values = new[] { 0f, 0.125f, 1.5f, 15.875f, 31.996f };

            foreach (var value in values)
            {
                var encoded = TestSharedDecalSystem.EncodeCoord(value);
                var decoded = TestSharedDecalSystem.DecodeCoord(encoded);
                Assert.That(MathF.Abs(decoded - value), Is.LessThanOrEqualTo(1f / TestSharedDecalSystem.Scale));
            }
        }

        [Test]
        public void QuantizationClampsNegativesToZero()
        {
            // Floating-point drift at chunk boundaries can push relative coords slightly negative.
            // We clamp to 0 instead of wrapping into ushort.MaxValue.
            Assert.That(TestSharedDecalSystem.EncodeCoord(-0.001f), Is.EqualTo((ushort) 0));
            Assert.That(TestSharedDecalSystem.EncodeCoord(-100f), Is.EqualTo((ushort) 0));
        }

        [Test]
        public void QuantizationClampsLargeValuesToUshortMax()
        {
            // Mostly defensive — relative coords should never reach this range, but verify the clamp.
            Assert.That(TestSharedDecalSystem.EncodeCoord(1_000_000f), Is.EqualTo(ushort.MaxValue));
        }

        [Test]
        public void NetDeltaPayloadStoresCompactDecalData()
        {
            var delta = new DecalChunkDelta
            {
                ResetChunk = true
            };

            delta.Upserts[1] = new NetDecalData
            {
                RelX = TestSharedDecalSystem.EncodeCoord(1.25f),
                RelY = TestSharedDecalSystem.EncodeCoord(2.5f),
                PrototypeNetId = 3,
                Color = Color.Red,
                Angle = Angle.FromDegrees(90),
                ZIndex = 4,
                Cleanable = true
            };
            delta.RemovedDecals.Add(77);

            Assert.That(delta.ResetChunk, Is.True);
            Assert.That(delta.Upserts[1].PrototypeNetId, Is.EqualTo((ushort) 3));
            Assert.That(TestSharedDecalSystem.DecodeCoord(delta.Upserts[1].RelX), Is.EqualTo(1.25f).Within(1f / TestSharedDecalSystem.Scale));
            Assert.That(delta.RemovedDecals, Contains.Item(77));
        }

        [Test]
        public void NetDecalEqualsDetectsAllFieldChanges()
        {
            var baseline = MakeNet(1, 2, 3, Color.White, 0, 5, false);

            Assert.That(SharedDecalSystem.NetDecalEquals(baseline, baseline), Is.True);

            Assert.That(SharedDecalSystem.NetDecalEquals(baseline, MakeNet(99, 2, 3, Color.White, 0, 5, false)), Is.False);
            Assert.That(SharedDecalSystem.NetDecalEquals(baseline, MakeNet(1, 99, 3, Color.White, 0, 5, false)), Is.False);
            Assert.That(SharedDecalSystem.NetDecalEquals(baseline, MakeNet(1, 2, 99, Color.White, 0, 5, false)), Is.False);
            Assert.That(SharedDecalSystem.NetDecalEquals(baseline, MakeNet(1, 2, 3, Color.Red, 0, 5, false)), Is.False);
            Assert.That(SharedDecalSystem.NetDecalEquals(baseline, MakeNet(1, 2, 3, Color.White, 90, 5, false)), Is.False);
            Assert.That(SharedDecalSystem.NetDecalEquals(baseline, MakeNet(1, 2, 3, Color.White, 0, 99, false)), Is.False);
            Assert.That(SharedDecalSystem.NetDecalEquals(baseline, MakeNet(1, 2, 3, Color.White, 0, 5, true)), Is.False);
        }

        [Test]
        public void DiffSnapshotsFirstSendMarksEverythingAsUpsert()
        {
            var current = new Dictionary<uint, NetDecalData>
            {
                [1] = MakeNet(10, 10, 0, Color.White, 0, 0, false),
                [2] = MakeNet(20, 20, 1, Color.Red, 0, 0, false),
            };

            var upserts = new Dictionary<uint, NetDecalData>();
            var removed = new List<uint>();
            SharedDecalSystem.DiffDecalSnapshots(current, previous: null, upserts, removed);

            Assert.That(upserts.Count, Is.EqualTo(2));
            Assert.That(upserts.ContainsKey(1), Is.True);
            Assert.That(upserts.ContainsKey(2), Is.True);
            Assert.That(removed, Is.Empty);
        }

        [Test]
        public void DiffSnapshotsNoChangeProducesEmptyDelta()
        {
            var snapshot = new Dictionary<uint, NetDecalData>
            {
                [1] = MakeNet(10, 10, 0, Color.White, 0, 0, false),
            };

            // Pass the same snapshot as both current and previous.
            var previous = new Dictionary<uint, NetDecalData>(snapshot);
            var upserts = new Dictionary<uint, NetDecalData>();
            var removed = new List<uint>();
            SharedDecalSystem.DiffDecalSnapshots(snapshot, previous, upserts, removed);

            Assert.That(upserts, Is.Empty);
            Assert.That(removed, Is.Empty);
        }

        [Test]
        public void DiffSnapshotsDetectsChangedFieldAsUpsert()
        {
            var previous = new Dictionary<uint, NetDecalData>
            {
                [1] = MakeNet(10, 10, 0, Color.White, 0, 0, false),
            };
            var current = new Dictionary<uint, NetDecalData>
            {
                [1] = MakeNet(10, 10, 0, Color.Red, 0, 0, false), // color changed
            };

            var upserts = new Dictionary<uint, NetDecalData>();
            var removed = new List<uint>();
            SharedDecalSystem.DiffDecalSnapshots(current, previous, upserts, removed);

            Assert.That(upserts.Count, Is.EqualTo(1));
            Assert.That(upserts[1].Color, Is.EqualTo(Color.Red));
            Assert.That(removed, Is.Empty);
        }

        [Test]
        public void DiffSnapshotsDetectsRemovals()
        {
            var previous = new Dictionary<uint, NetDecalData>
            {
                [1] = MakeNet(10, 10, 0, Color.White, 0, 0, false),
                [2] = MakeNet(20, 20, 0, Color.White, 0, 0, false),
            };
            var current = new Dictionary<uint, NetDecalData>
            {
                [1] = MakeNet(10, 10, 0, Color.White, 0, 0, false),
                // 2 removed
            };

            var upserts = new Dictionary<uint, NetDecalData>();
            var removed = new List<uint>();
            SharedDecalSystem.DiffDecalSnapshots(current, previous, upserts, removed);

            Assert.That(upserts, Is.Empty);
            Assert.That(removed, Is.EquivalentTo(new uint[] { 2 }));
        }

        [Test]
        public void DiffSnapshotsHandlesMixedAddRemoveModify()
        {
            var previous = new Dictionary<uint, NetDecalData>
            {
                [1] = MakeNet(10, 10, 0, Color.White, 0, 0, false), // unchanged
                [2] = MakeNet(20, 20, 0, Color.White, 0, 0, false), // will be modified
                [3] = MakeNet(30, 30, 0, Color.White, 0, 0, false), // will be removed
            };
            var current = new Dictionary<uint, NetDecalData>
            {
                [1] = MakeNet(10, 10, 0, Color.White, 0, 0, false),
                [2] = MakeNet(20, 20, 0, Color.Red, 0, 0, false),    // modified
                [4] = MakeNet(40, 40, 0, Color.White, 0, 0, false),  // added
            };

            var upserts = new Dictionary<uint, NetDecalData>();
            var removed = new List<uint>();
            SharedDecalSystem.DiffDecalSnapshots(current, previous, upserts, removed);

            Assert.That(upserts.Keys, Is.EquivalentTo(new uint[] { 2, 4 }));
            Assert.That(removed, Is.EquivalentTo(new uint[] { 3 }));
        }

        private static NetDecalData MakeNet(ushort relX, ushort relY, ushort protoId, Color color, double angleDeg, int zIndex, bool cleanable)
        {
            return new NetDecalData
            {
                RelX = relX,
                RelY = relY,
                PrototypeNetId = protoId,
                Color = color,
                Angle = Angle.FromDegrees(angleDeg),
                ZIndex = zIndex,
                Cleanable = cleanable,
            };
        }
    }

    internal sealed class TestSharedDecalSystem : SharedDecalSystem
    {
        public const float Scale = DecalCoordQuantScale;

        public static ushort EncodeCoord(float value) => QuantizeDecalCoord(value);
        public static float DecodeCoord(ushort value) => DequantizeDecalCoord(value);
    }
}
