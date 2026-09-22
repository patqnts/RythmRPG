using NUnit.Framework;
using UnityEngine;

namespace RythmRPG.Combat.Tests
{
    public sealed class CombatHudTests
    {
        [Test]
        public void ResourceBar_ClampsValues()
        {
            var parent = new GameObject("Hud Test", typeof(RectTransform));
            try
            {
                ResourceBarView bar = ResourceBarView.CreateTemplate(parent.transform, "Bar", new ResourceBarStyle(), null);
                bar.Set(150, 100, false);
                Assert.That(bar.Current, Is.EqualTo(100));
                Assert.That(bar.Normalized, Is.EqualTo(1f));
                bar.Set(-5, 100, false);
                Assert.That(bar.Current, Is.EqualTo(0));
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void ResourceBar_ReportsChangesAfterTheFirstValue()
        {
            var parent = new GameObject("Hud Test", typeof(RectTransform));
            try
            {
                ResourceBarView bar = ResourceBarView.CreateTemplate(parent.transform, "Bar", new ResourceBarStyle(), null);
                int calls = 0, previous = -1, current = -1;
                bar.ValueChanged += (p, c, _) => { calls++; previous = p; current = c; };
                bar.Set(100, 100, false);
                Assert.That(calls, Is.EqualTo(0), "the first value is a snap, not a change");
                bar.Set(40, 100);
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(previous, Is.EqualTo(100));
                Assert.That(current, Is.EqualTo(40));
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void AbilitySlot_ShowIsIdempotentAndHideWorks()
        {
            var slot = new GameObject("Slot Test");
            try
            {
                AbilitySlotView view = slot.AddComponent<AbilitySlotView>();
                view.SetVisible(true, true, 0.5f, 0f);
                Assert.That(view.IsShown, Is.True);
                view.SetVisible(true, false);
                Assert.That(view.IsShown, Is.True, "a second show must not restart or cut the pop-up");
                view.SetVisible(false, false);
                Assert.That(view.IsShown, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(slot);
            }
        }
    }
}
