using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using GMap.NET;

namespace MapsWPF.Services.Reporting
{
    public class ReportService
    {
        private readonly TemplateService _templates;
        private readonly MapService _mapService;
        private readonly NotificationService _notification;
        private readonly ClipboardService _clipboard;
        private readonly ISelectionProvider _selection;
        private readonly IReportOutput _output;

        private float? _lastAzimuth = null;
        private bool _isSingleLineFormat = false;
        private bool _isReportTemplate = false;

        public ReportController(TemplateService templates, MapService mapService, NotificationService notification, ClipboardService clipboard, ISelectionProvider selection, IReportOutput output)
        {
            _templates = templates ?? throw new ArgumentNullException(nameof(templates));
            _mapService = mapService ?? throw new ArgumentNullException(nameof(mapService));
            _notification = notification ?? throw new ArgumentNullException(nameof(notification));
            _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
            _selection = selection ?? throw new ArgumentNullException(nameof(selection));
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        public string GenerateTextFromTemplate(List<string> template)
        {
            string selectedPosition = _selection.GetSelectedPosition();
            string selectedPilot = _selection.GetSelectedPilot();
            string selectedDrone = _selection.GetSelectedDrone();
            string selectedLocalCities = string.Join(", ", _selection.GetLocalCities());
            string shootingTargets = string.IsNullOrWhiteSpace(_selection.GetShootingTarget()) || _selection.GetShootingTarget() == "Патрулювання"
                                        ? "Патрулювання" : _selection.GetShootingTarget().Trim();
            string selectedheight = _selection.GetHeightText().Trim();
            string selectedrange2 = _selection.GetSelectedRange().ToString();

            PointF utm;
            string mgrsShort;
            string nearestLocality;

            if (_selection.TryGetUTM(out utm))
            {
                mgrsShort = _selection.FormatShortMGRSFromUTM(utm);
                nearestLocality = _selection.FindClosestLocalityFromLatLng(new PointLatLng(utm.Y, utm.X)).ToUpper(new CultureInfo("uk-UA"));
            }
            else if (_selection.TryGetClickedLatLng(out var clickedLatLng))
            {
                mgrsShort = _selection.FormatShortMGRSFromLatLng(clickedLatLng);
                nearestLocality = _selection.FindClosestLocalityFromLatLng(clickedLatLng).ToUpper(new CultureInfo("uk-UA"));
            }
            else
            {
                return string.Empty;
            }

            string selectedTarget = _selection.GetTargetType();
            string timeString_ = string.IsNullOrEmpty(_selection.GetCurrentTimeString()) ? "" : _selection.GetCurrentTimeString().Replace(':', '.');

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
                ["{UnitName}"] = _templates.CustomUnit,
                ["{LaunchArea}"] = _templates.LaunchArea ?? ""
            };

            if (template == _templates.EndWorkShablon)
            {
                string targetResult = _selection.IsTargetDestroyed()
                    ? $"Ціль знищено {selectedTarget}."
                    : "Ціль не знищено.";

                string boardResult = _selection.IsTargetBoardLost()
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
                if (_selection.IsTargetDestroyed())
                {
                    targetStatus = "знищено";
                    if (_selection.IsTargetBoardLost())
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
                    if (_selection.IsTargetBoardLost())
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
            if (!_selection.TryGetUTM(out _)) return;

            _isSingleLineFormat = false;
            _selection.GetAndSetTime();
            string generatedText = GenerateTextFromTemplate(_templates.StartWorkShablon);
            _output.SetReportText(generatedText);
            _output.CopyToClipboardWithNotification(generatedText);
        }

        public void End_of_Work_Click(object sender, EventArgs e)
        {
            if (!_selection.TryGetUTM(out _)) return;

            _selection.GetAndSetTime();
            string generatedText = GenerateTextFromTemplate(_templates.EndWorkShablon);
            _output.SetReportText(generatedText);
            _output.CopyToClipboardWithNotification(generatedText);
        }

        public void Combat_Work_Click(object sender, EventArgs e)
        {
            var clicked = _selection.GetClickedPoint();
            if (clicked.HasValue && _selection.GetAttackPoint() != PointF.Empty)
            {
                float azimuth = CalculateAzimuth(_selection.GetAttackPoint(), clicked.Value);
                _lastAzimuth = azimuth;
                _output.SetAzimuthDisplay(Math.Round(azimuth).ToString(CultureInfo.InvariantCulture) + "°");
            }
            else
            {
                string rawAzimuth = (_selection.GetAzimuthText() ?? string.Empty).Replace("°", "").Trim();
                if (string.IsNullOrEmpty(rawAzimuth) || !float.TryParse(rawAzimuth, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    _output.SetReportText("Помилка: неможливо зчитати значення азимута!");
                    return;
                }
                _lastAzimuth = parsed;
                _output.SetAzimuthDisplay(Math.Round(parsed).ToString(CultureInfo.InvariantCulture) + "°");
            }

            if ((_selection.GetClickedPoint().HasValue || _lastAzimuth.HasValue) && _templates.ReportWorkShablonActual != null)
            {
                _isReportTemplate = true;
                _isSingleLineFormat = true;
                string generatedText = GenerateTextFromTemplate(_templates.ReportWorkShablonActual);
                _output.SetReportText(generatedText);
                _isReportTemplate = false;
                _output.CopyToClipboardWithNotification(generatedText);
            }
            else
            {
                _output.SetReportText("Вкажіть точку польоту на карті!");
            }
        }

        private float CalculateAzimuth(PointF fromPoint, PointF toPoint)
        {
            float dx = toPoint.X - fromPoint.X;
            float dy = toPoint.Y - fromPoint.Y;
            float angleRad = MathF.Atan2(dx, dy);
            float angleDeg = (angleRad * (180f / MathF.PI) + 360f) % 360f;
            return angleDeg;
        }
    }
}