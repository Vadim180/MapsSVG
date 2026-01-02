using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using MapsWPF.Services;
using GMap.NET;

namespace MapsWPF.Controllers
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
        bool TryGetUTM(out PointF utm);
        string FormatShortMGRSFromUTM(PointF utm);
        string FindClosestLocality(PointF utm);

        bool TryGetClickedLatLng(out PointLatLng latlng);
        string FormatShortMGRSFromLatLng(PointLatLng latlng);
        string FindClosestLocalityFromLatLng(PointLatLng latlng);

        string GetTargetType();
        string GetAndSetTime();
        string GetCurrentTimeString();
        TemplateService GetShablon();
        bool IsTargetDestroyed();
        bool IsTargetBoardLost();
        void SetReportText(string text);
        void CopyToClipboardWithNotification(string text);
        PointF? GetClickedPoint();
        PointF GetAttackPoint();
        string GetLabelScaleText();
        string GetAzimuthText();
        void SetAzimuthDisplay(string text);
    }

    public class ReportController
    {
        private readonly IReportContext _ctx;
        private float? _lastAzimuth = null;
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

            PointF utm;
            string mgrsShort;
            string nearestLocality;

            if (_ctx.TryGetUTM(out utm))
            {
                mgrsShort = _ctx.FormatShortMGRSFromUTM(utm);
                nearestLocality = _ctx.FindClosestLocality(utm).ToUpper(new CultureInfo("uk-UA"));
            }
            else if (_ctx.TryGetClickedLatLng(out var clickedLatLng))
            {
                mgrsShort = _ctx.FormatShortMGRSFromLatLng(clickedLatLng);
                nearestLocality = _ctx.FindClosestLocalityFromLatLng(clickedLatLng).ToUpper(new CultureInfo("uk-UA"));
            }
            else
            {
                // No selected point available
                return string.Empty;
            }

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
                ["{azimyth}"] = _lastAzimuth.HasValue ? Math.Round(_lastAzimuth.Value).ToString(CultureInfo.InvariantCulture) : "0",
                ["{range}"] = selectedrange2,
                ["{height}"] = selectedheight,
                ["{MGRS_Short}"] = mgrsShort,
                ["{CurrentCoordMGRS}"] = mgrsShort,
                ["{CurrentCoordUTM}"] = utm != null ? $"{utm.X:F1}/{utm.Y:F1}" : string.Empty,
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
            var clicked = _ctx.GetClickedPoint();
            if (clicked.HasValue && _ctx.GetAttackPoint() != PointF.Empty)
            {
                float azimuth = CalculateAzimuth(_ctx.GetAttackPoint(), clicked.Value);
                _lastAzimuth = azimuth;
                // update UI display
                _ctx.SetAzimuthDisplay(Math.Round(azimuth).ToString(CultureInfo.InvariantCulture) + "°");
            }
            else
            {
                string rawAzimuth = (_ctx.GetAzimuthText() ?? string.Empty).Replace("°", "").Trim();
                if (string.IsNullOrEmpty(rawAzimuth) || !float.TryParse(rawAzimuth, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    _ctx.SetReportText("Помилка: неможливо зчитати значення азимута!");
                    return;
                }
                _lastAzimuth = parsed;
                _ctx.SetAzimuthDisplay(Math.Round(parsed).ToString(CultureInfo.InvariantCulture) + "°");
            }

            if ((_ctx.GetClickedPoint().HasValue || _lastAzimuth.HasValue) && _ctx.GetShablon().ReportWorkShablonActual != null)
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
                _ctx.SetReportText("Вкажіть точку польоту на карті!");
            }
        }

        private float CalculateAzimuth(PointF fromPoint, PointF toPoint)
        {
            // Compute azimuth in degrees where 0 = North, 90 = East, 180 = South, 270 = West
            float dx = toPoint.X - fromPoint.X; // Easting difference
            float dy = toPoint.Y - fromPoint.Y; // Northing difference
            // Use atan2(dx, dy) so that north (dx=0, dy>0) -> 0 deg, east (dx>0, dy=0) -> 90 deg
            float angleRad = MathF.Atan2(dx, dy);
            float angleDeg = (angleRad * (180f / MathF.PI) + 360f) % 360f;
            return angleDeg;
        }
    }
}