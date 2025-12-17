using System.Drawing;
using Maps.Controllers;
using Xunit;

namespace Maps.Tests
{
    public class InputControllerTests
    {
        private class FakeContext : IInputContext
        {
            public bool IsMainTabActive => true;
            public float Angle = 0f;
            public float Servo = 0f;
            public bool SafeInvalidated = false;
            public bool MapInvalidated = false;

            public bool HasAttackPoint() => true;
            public void SetInteracting(bool value) { }
            public void StartTimer() { }
            public void StopTimer() { }
            public void AdjustAttackAngle(float delta) { Angle += delta; Angle = (Angle % 360 + 360) % 360; }
            public void AdjustServoAngle(float delta) { Servo += delta; }
            public float GetAttackAngle() => Angle;
            public RectangleF GetAttackZoneBounds() => RectangleF.Empty;
            public void UpdateAngleDisplays(float angle, float servo) { /* no-op */ }
            public void SafeInvalidate(RectangleF bounds) { SafeInvalidated = true; }
            public void InvalidateMap() { MapInvalidated = true; }
        }

        [Fact]
        public void TimerTick_DecreasesAngle_WhenLeftPressed()
        {
            var ctx = new FakeContext { Angle = 10f };
            var ctrl = new InputController(ctx);

            // Simulate pressing left
            ctrl.OnKeyDown(null, new System.Windows.Forms.KeyEventArgs(System.Windows.Forms.Keys.A));

            // One tick
            ctrl.Timer_Tick(null, System.EventArgs.Empty);

            Assert.Equal(9f, ctx.Angle);
            Assert.True(ctx.SafeInvalidated);
            Assert.True(ctx.MapInvalidated);
        }

        [Fact]
        public void TimerTick_IncreasesAngle_WhenRightPressed()
        {
            var ctx = new FakeContext { Angle = 350f };
            var ctrl = new InputController(ctx);

            // Simulate pressing right
            ctrl.OnKeyDown(null, new System.Windows.Forms.KeyEventArgs(System.Windows.Forms.Keys.D));

            // One tick
            ctrl.Timer_Tick(null, System.EventArgs.Empty);

            Assert.Equal(351f % 360, ctx.Angle);
        }

        [Fact]
        public void KeyUp_StopsRotation()
        {
            var ctx = new FakeContext { Angle = 100f };
            var ctrl = new InputController(ctx);

            ctrl.OnKeyDown(null, new System.Windows.Forms.KeyEventArgs(System.Windows.Forms.Keys.A));
            ctrl.OnKeyUp(null, new System.Windows.Forms.KeyEventArgs(System.Windows.Forms.Keys.A));
            ctrl.Timer_Tick(null, System.EventArgs.Empty);

            // Angle should remain unchanged after key up
            Assert.Equal(100f, ctx.Angle);
        }
    }
}
