using System;
using System.Drawing;

namespace Maps.Controllers
{
    public interface IInputContext
    {
        bool IsMainTabActive { get; }
        bool HasAttackPoint();
        void SetInteracting(bool value);
        void StartTimer();
        void StopTimer();
        void AdjustAttackAngle(float delta);
        void AdjustServoAngle(float delta);
        void AdjustSectorWidth(float delta);
        float GetAttackAngle();
        float GetSectorWidth();
        RectangleF GetAttackZoneBounds();
        void UpdateAngleDisplays(float angle, float servo);
        void SafeInvalidate(RectangleF bounds);
        void InvalidateMap();
        void InvalidateMapImmediate();
    }

    public class InputController
    {
        private readonly IInputContext _ctx;
        private bool _isRotateLeftPressed = false;
        private bool _isRotateRightPressed = false;
        private bool _isExpandPressed = false;
        private bool _isNarrowPressed = false;
        private double _rotationSpeedDegPerSec = 90.0; // default rotation speed (deg/sec)
        private double _sectorWidthSpeedDegPerSec = 45.0; // default sector width change speed (deg/sec)
        private readonly ITimeProvider _timeProvider;
        private long _lastTickMs;

        public InputController(IInputContext ctx, ITimeProvider? timeProvider = null)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            _timeProvider = timeProvider ?? new SystemTimeProvider();
            _lastTickMs = _timeProvider.GetMilliseconds();
        }

        public void SetRotationSpeedDegPerSec(double degPerSec) => _rotationSpeedDegPerSec = degPerSec;

        public void OnKeyDown(object? sender, System.Windows.Forms.KeyEventArgs e)
        {
            // Shortcut preserved for backward compatibility: Ctrl+Shift+S handled by host if desired
            if (_ctx == null) return;

            if (!_ctx.HasAttackPoint()) return;
            if (!_ctx.IsMainTabActive) return;

            bool isLeft = e.KeyCode == System.Windows.Forms.Keys.A || e.KeyCode == System.Windows.Forms.Keys.Left;
            bool isRight = e.KeyCode == System.Windows.Forms.Keys.D || e.KeyCode == System.Windows.Forms.Keys.Right;
            bool isExpand = e.KeyCode == System.Windows.Forms.Keys.W;
            bool isNarrow = e.KeyCode == System.Windows.Forms.Keys.S;

            if (isLeft || isRight || isExpand || isNarrow)
            {
                _ctx.SetInteracting(true);

                // Clear opposite direction to prevent conflicts
                if (isLeft)
                {
                    _isRotateLeftPressed = true;
                    _isRotateRightPressed = false;
                }
                else if (isRight)
                {
                    _isRotateRightPressed = true;
                    _isRotateLeftPressed = false;
                }

                if (isExpand)
                {
                    _isExpandPressed = true;
                    _isNarrowPressed = false;
                }
                else if (isNarrow)
                {
                    _isNarrowPressed = true;
                    _isExpandPressed = false;
                }

                // Reset time baseline to avoid huge dt on first tick
                _lastTickMs = _timeProvider.GetMilliseconds();

                _ctx.StartTimer();

                // Apply an immediate small step to provide instant visual feedback on key press
                ApplyImmediateStep();

                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void ApplyImmediateStep()
        {
            bool changed = false;

            // Determine rotation direction: left = negative (counter-clockwise), right = positive (clockwise)
            float angleDelta = 0f;
            if (_isRotateLeftPressed && !_isRotateRightPressed) angleDelta = -0.5f;
            else if (_isRotateRightPressed && !_isRotateLeftPressed) angleDelta = 0.5f;

            if (angleDelta != 0f)
            {
                _ctx.AdjustAttackAngle(angleDelta);
                _ctx.AdjustServoAngle(angleDelta);
                changed = true;
            }

            // Determine sector width change: expand = positive, narrow = negative
            float widthDelta = 0f;
            if (_isExpandPressed && !_isNarrowPressed) widthDelta = 0.5f;
            else if (_isNarrowPressed && !_isExpandPressed) widthDelta = -0.5f;

            if (widthDelta != 0f)
            {
                _ctx.AdjustSectorWidth(widthDelta);
                changed = true;
            }

            if (!changed) return;

            float angle = _ctx.GetAttackAngle();
            _ctx.UpdateAngleDisplays(angle, 0);

            try { _ctx.InvalidateMapImmediate(); } catch { }
        }

        public void OnKeyUp(object? sender, System.Windows.Forms.KeyEventArgs e)
        {
            if (!_ctx.IsMainTabActive) return;

            if (e.KeyCode == System.Windows.Forms.Keys.A || e.KeyCode == System.Windows.Forms.Keys.Left) _isRotateLeftPressed = false;
            if (e.KeyCode == System.Windows.Forms.Keys.D || e.KeyCode == System.Windows.Forms.Keys.Right) _isRotateRightPressed = false;
            if (e.KeyCode == System.Windows.Forms.Keys.W) _isExpandPressed = false;
            if (e.KeyCode == System.Windows.Forms.Keys.S) _isNarrowPressed = false;

            if (!_isRotateLeftPressed && !_isRotateRightPressed && !_isExpandPressed && !_isNarrowPressed)
            {
                _ctx.StopTimer();
                _ctx.SetInteracting(false);
            }

            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        public void Timer_Tick(object? sender, EventArgs e)
        {
            if ((!_isRotateLeftPressed && !_isRotateRightPressed && !_isExpandPressed && !_isNarrowPressed) || !_ctx.IsMainTabActive)
            {
                _ctx.StopTimer();
                return;
            }

            long now = _timeProvider.GetMilliseconds();
            long dt = now - _lastTickMs;
            _lastTickMs = now;

            // Clamp dt to reasonable range to prevent jumps (e.g., if timer was delayed)
            if (dt <= 0) return;
            if (dt > 100) dt = 16; // cap at ~60fps worth if timer was blocked

            bool changed = false;

            // Handle rotation
            double rotationSign = 0.0;
            if (_isRotateLeftPressed && !_isRotateRightPressed) rotationSign = -1.0;
            else if (_isRotateRightPressed && !_isRotateLeftPressed) rotationSign = 1.0;

            if (rotationSign != 0.0)
            {
                double deltaDeg = _rotationSpeedDegPerSec * (dt / 1000.0) * rotationSign;
                _ctx.AdjustAttackAngle((float)deltaDeg);
                _ctx.AdjustServoAngle((float)deltaDeg);
                changed = true;
            }

            // Handle sector width
            double widthSign = 0.0;
            if (_isExpandPressed && !_isNarrowPressed) widthSign = 1.0;
            else if (_isNarrowPressed && !_isExpandPressed) widthSign = -1.0;

            if (widthSign != 0.0)
            {
                double deltaWidth = _sectorWidthSpeedDegPerSec * (dt / 1000.0) * widthSign;
                _ctx.AdjustSectorWidth((float)deltaWidth);
                changed = true;
            }

            if (!changed) return;

            // Update UI
            float angle = _ctx.GetAttackAngle();
            _ctx.UpdateAngleDisplays(angle, 0);

            // immediate overlay refresh for smooth visual feedback
            try { _ctx.InvalidateMapImmediate(); } catch { }
        }

        // Small time provider abstraction for tests
        public interface ITimeProvider { long GetMilliseconds(); }
        private class SystemTimeProvider : ITimeProvider { public long GetMilliseconds() => Environment.TickCount; }
    }
}
