using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using Maps.Services;

namespace Maps.Controllers
{
    public interface IReportContext
    {
        string GetSelectedPosition();
        string GetSelectedPilot();
        string GetSelectedDrone();
        IEnumerable<string> GetLocalCities();
        string GetShootingTarget();
        string GetHeightText();
        int GetSelectedRange();
        bool TryGetUTM(out PointF utm); // shows messagebox if no point selected
        string FormatShortMGRSFromUTM(PointF utm);
        string FindClosestLocality(PointF utm);
        int GetCourseValue();
        string GetTargetType();
        string GetAndSetTime(); // sets internal start time and returns formatted time range
        string GetCurrentTimeString();
        ShablonManager GetShablon();
        bool IsTargetDestroyed();
        bool IsTargetBoardLost();
        void SetReportText(string text);
        void CopyToClipboardWithNotification(string text);
        void LogFlightIfValid();
        PointF? GetClickedPoint();
        PointF GetAttackPoint();
        string GetLabelScaleText();
    }

    public class ReportController
    {
        private readonly IReportContext _ctx;
        private bool _isSingleLineFormat = false;
        private bool _isReportTemplate = false;

        public ReportController(IReportContext ctx)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        }

        public string GenerateTextFromTemplate(List<string> template)
        {
            string selectedPosition = _ctx.GetSelectedPosition();
            string selectedPilot = _ctx.GetSelectedPilot();
            string selectedDrone = _ctx.GetSelectedDrone();
            string selectedLocalCities = string.Join(", ", _ctx.GetLocalCities());
            string shootingTargets = string.IsNullOrWhiteSpace(_ctx.GetShootingTarget()) || _ctx.GetShootingTarget() == "Патрулювання"
                                        ? "Патрулювання" : _ctx.GetShootingTarget().Trim();
            string selectedheight = _ctx.GetHeightText().Trim();
            string selectedrange2 = _ctx.GetSelectedRange().ToString();

            if (!_ctx.TryGetUTM(out PointF utm)) return string.Empty;

            string mgrsShort = _ctx.FormatShortMGRSFromUTM(utm);
            string nearestLocality = _ctx.FindClosestLocality(utm).ToUpper(new CultureInfo("uk-UA"));
            string courseStr = _ctx.GetCourseValue().ToString();
            string selectedTarget = _ctx.GetTargetType();
            string timeString_ = string.IsNullOrEmpty(_ctx.GetCurrentTimeString()) ? "" : _ctx.GetCurrentTimeString().Replace(':', '.');

            string targetStatus = "";
            string expenses = "";
            string additionalInfo = "";

            var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["{Position}"] = selectedPosition,
                ["{Pilot}"] = selectedPilot,
                ["{DroneBy}"] = selectedDrone,
                ["{Time}"] = timeString_,
                ["{LocalCiti}"] = selectedLocalCities,
                ["{ShootingTarget}"] = shootingTargets,
                ["{azimyth}"] = "0",
                ["{range}"] = selectedrange2,
                ["{height}"] = selectedheight,
                ["{MGRS_Short}"] = mgrsShort,
                ["{course}"] = courseStr,
                ["{nearestLocality}"] = nearestLocality,
                ["{TargetType}"] = selectedTarget,
                ["{UnitName}"] = _ctx.GetShablon().CustomUnit,
                ["{LaunchArea}"] = _ctx.GetShablon().LaunchArea ?? ""
            };

            // Special handling for EndWork template
            if (template == _ctx.GetShablon().EndWorkShablon)
            {
                string targetResult = _ctx.IsTargetDestroyed()
                    ? $"Ціль знищено {selectedTarget}."
                    : "Ціль не знищено.";

                string boardResult = _ctx.IsTargetBoardLost()
                    ? "Борт втрачено."
                    : "Борт повернуто.";

                replacements["{TargetStatus}"] = $"{targetResult} {boardResult}";

                var finalLines = new List<string>(template.Count);
                foreach (var raw in template)
                {
                    finalLines.Add(ApplyReplacements(raw, replacements));
                }

                return string.Join(Environment.NewLine, finalLines);
            }
            else
            {
                if (_ctx.IsTargetDestroyed())
                {
                    targetStatus = "знищено";
                    if (_ctx.IsTargetBoardLost())
                    {
                        expenses = "Втрати: 1 FPV.";
                        additionalInfo = "Довідково: Підрив біля цілі, ";
                    }
                    else
                    {
                        expenses = "Втрати: Дрон повернуто.";
                        additionalInfo = "Довідково: ціль не виявлено, ";
                    }
                }
                else
                {
                    targetStatus = "не знищено";
                    if (_ctx.IsTargetBoardLost())
                    {
                        expenses = "Втрати: 1 FPV.";
                        additionalInfo = "Довідково: Технічні несправності, ";
                    }
                    else
                    {
                        expenses = "Втрати: Дрон повернуто.";
                        additionalInfo = "Довідково: ціль не виявлено, ";
                    }
                }

                replacements["{TargetStatus}"] = targetStatus;
                replacements["{Expenses}"] = expenses;
                replacements["{AdditionalInfo}"] = additionalInfo;

                var finalLines = new List<string>(template.Count);
                foreach (var raw in template)
                {
                    finalLines.Add(ApplyReplacements(raw, replacements));
                }

                return _isSingleLineFormat
                    ? string.Join(" ", finalLines)
                    : string.Join(Environment.NewLine, finalLines);
            }
        }

        private string ApplyReplacements(string input, Dictionary<string, string> replacements)
        {
            if (string.IsNullOrEmpty(input) || replacements == null)
                return input;

            foreach (var kv in replacements)
                input = input.Replace(kv.Key, kv.Value);
            return input;
        }

        public void Start_of_Work_Click(object sender, EventArgs e)
        {
            if (!_ctx.TryGetUTM(out _)) return; // shows message box if no point

            _isSingleLineFormat = false;
            _ctx.GetAndSetTime();
            string generatedText = GenerateTextFromTemplate(_ctx.GetShablon().StartWorkShablon);
            _ctx.SetReportText(generatedText);
            _ctx.CopyToClipboardWithNotification(generatedText);
        }

        public void End_of_Work_Click(object sender, EventArgs e)
        {
            if (!_ctx.TryGetUTM(out _)) return;

            _ctx.GetAndSetTime();
            string generatedText = GenerateTextFromTemplate(_ctx.GetShablon().EndWorkShablon);
            _ctx.SetReportText(generatedText);
            _ctx.CopyToClipboardWithNotification(generatedText);
        }

        public void Combat_Work_Click(object sender, EventArgs e)
        {
            _ctx.LogFlightIfValid();

            var clicked = _ctx.GetClickedPoint();
            if (clicked.HasValue && _ctx.GetAttackPoint() != PointF.Empty)
            {
                float azimuth = CalculateAzimuth(_ctx.GetAttackPoint(), clicked.Value);
                // azimuth could be integrated into replacements if needed
            }
            else
            {
                string rawAzimuth = _ctx.GetLabelScaleText().Replace("Кут: ", "").Replace("°", "").Trim();
                if (!float.TryParse(rawAzimuth, out float _))
                {
                    System.Windows.Forms.MessageBox.Show("Помилка: неможливо зчитати значення азимута!", "Помилка",
                                   System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                    return;
                }
            }

            if (clicked.HasValue && _ctx.GetShablon().ReportWorkShablonActual != null)
            {
                _isReportTemplate = true;
                _isSingleLineFormat = true;
                string generatedText = GenerateTextFromTemplate(_ctx.GetShablon().ReportWorkShablonActual);
                _ctx.SetReportText(generatedText);
                _isReportTemplate = false;
                _ctx.CopyToClipboardWithNotification(generatedText);
            }
            else
            {
                System.Windows.Forms.MessageBox.Show("Вкажіть точку польоту на карті!", "Увага",
                               System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Warning);
            }
        }

        private float CalculateAzimuth(PointF fromPoint, PointF toPoint)
        {
            float dx = toPoint.X - fromPoint.X;
            float dy = toPoint.Y - fromPoint.Y;
            float angleRad = MathF.Atan2(dy, dx);
            float angleDeg = (angleRad * (180f / MathF.PI)) + 90;
            angleDeg = (int)(angleDeg + 360f) % 360f;
            return angleDeg;
        }
    }
}
