using GMap.NET;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows;

namespace MapsWPF.Services
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

        public ReportService(TemplateService templates,MapService mapService,NotificationService notification,ClipboardService clipboard,ISelectionProvider selection,IReportOutput output)
        {
            _templates = templates ?? throw new ArgumentNullException(nameof(templates));
            _mapService = mapService ?? throw new ArgumentNullException(nameof(mapService));
            _notification = notification ?? throw new ArgumentNullException(nameof(notification));
            _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
            _selection = selection ?? throw new ArgumentNullException(nameof(selection));
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        public TemplateService TemplateService => _templates;

        public string GenerateTextFromTemplate(List<string> template)
        {
            string selectedPosition = _selection.GetSelectedPosition();
            string selectedPilot = _selection.GetSelectedPilot();
            string selectedDrone = _selection.GetSelectedDrone();
            string selectedDirections = string.Join(", ",_selection.GetSelectedFlyDirections());
            string shootingTargets = _selection.GetShootingTarget()?.Trim() ?? string.Empty;
            string selectedheight = _selection.GetHeightText().Trim();
            string selectedrange2 = _selection.GetSelectedRange().ToString();

            PointF utm = PointF.Empty;
            string mgrsShort = string.Empty;
            string nearestLocality = string.Empty;

            if (_selection.TryGetClickedLatLng(out var clickedLatLng))
            {
                if (_selection.TryGetUTM(out utm))
                {
                    mgrsShort = _selection.FormatShortMGRSFromUTM(utm);
                }
                else
                {
                    mgrsShort = _selection.FormatShortMGRSFromLatLng(clickedLatLng);
                }

                var nearestLocalityRaw =
                    _selection.FindClosestLocalityFromLatLng(clickedLatLng);

                nearestLocality = string.IsNullOrWhiteSpace(nearestLocalityRaw)
                    ? string.Empty
                    : nearestLocalityRaw.ToUpper(new CultureInfo("uk-UA"));
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
                ["{ShootingTarget}"] = shootingTargets,
                ["{azimyth}"] = _lastAzimuth.HasValue ? Math.Round(_lastAzimuth.Value).ToString(CultureInfo.InvariantCulture) : "0",
                ["{range}"] = selectedrange2,
                ["{height}"] = selectedheight,
                ["{MGRS_Short}"] = mgrsShort,
                ["{CurrentCoordMGRS}"] = mgrsShort,
                ["{CurrentCoordUTM}"] = utm == PointF.Empty? string.Empty: $"{utm.X:F1}/{utm.Y:F1}",
                ["{nearestLocality}"] = nearestLocality,
                ["{TargetType}"] = selectedTarget,
                ["{UnitName}"] = string.Empty,
                ["{Frequencies}"] = _templates.Frequencies ?? string.Empty,
                ["{Purpose}"] = _templates.Purpose ?? string.Empty,
                ["{Direction}"] = selectedDirections
            };

            if (template == _templates.EndWorkShablon)
            {
                string targetResult = _selection.IsTargetDestroyed()
                    ? (_templates.DefaultTargetStatus ?? "Ціль знищено.")
                    : "Ціль не знищено.";

                string boardResult = _selection.IsTargetBoardLost()
                    ? ((_templates.DefaultExpenses ?? "Борт втрачено") + ".")
                    : "Борт повернуто.";

                // Set both placeholders explicitly for EndWork
                replacements["{TargetStatus}"] = targetResult;
                replacements["{Expenses}"] = boardResult;

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
                    targetStatus = _templates.DefaultTargetStatus ?? "Ціль знищено.";
                    if (_selection.IsTargetBoardLost())
                    {
                        expenses = _templates.DefaultExpenses ?? "Борт втрачено";
                        additionalInfo = "Довідково: Підрив біля цілі, ";
                    }
                    else
                    {
                        expenses = "Борт повернуто.";
                        additionalInfo = "Довідково: ціль не виявлено, ";
                    }
                }
                else
                {
                    targetStatus = "Ціль не знищено.";
                    if (_selection.IsTargetBoardLost())
                    {
                        expenses = _templates.DefaultExpenses ?? "Борт втрачено";
                        additionalInfo = "Довідково: Технічні несправності, ";
                    }
                    else
                    {
                        expenses = "Борт повернуто.";
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

        private static bool TemplateContains(List<string> template, string placeholder)
        {
            if (template == null || template.Count == 0)
            {
                return false;
            }

            return template.Any(line =>
                !string.IsNullOrWhiteSpace(line) &&
                line.Contains(placeholder, StringComparison.Ordinal)
            );
        }

        private bool HasAnySelectedDirection()
        {
            var directions = _selection.GetSelectedFlyDirections();

            return directions != null &&
                   directions.Any(x => !string.IsNullOrWhiteSpace(x));
        }

        private bool TryValidateRequiredFields(
            string templateName,
            List<string> template,
            out string message)
        {
            message = string.Empty;

            if (template == null || template.Count == 0)
            {
                message = "Шаблон порожній.";
                return false;
            }

            var name = templateName?.Trim() ?? string.Empty;

            if (string.Equals(name, "StartWork", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "EndWork", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Report", StringComparison.OrdinalIgnoreCase))
            {
                if (!HasFlightPoint())
                {
                    message = "Спочатку вибери точку польоту на карті.";
                    return false;
                }
            }

            if (string.Equals(name, "Report", StringComparison.OrdinalIgnoreCase))
            {
                if (!HasAttackPoint())
                {
                    message = "Спочатку встанови точку атаки.";
                    return false;
                }
            }

            if (TemplateContains(template, "{Position}") &&
                string.IsNullOrWhiteSpace(_selection.GetSelectedPosition()))
            {
                message = "Вибери позицію.";
                return false;
            }

            if (TemplateContains(template, "{Pilot}") &&
                string.IsNullOrWhiteSpace(_selection.GetSelectedPilot()))
            {
                message = "Вибери пілота.";
                return false;
            }

            if (TemplateContains(template, "{DroneBy}") &&
                string.IsNullOrWhiteSpace(_selection.GetSelectedDrone()))
            {
                message = "Вибери дрон.";
                return false;
            }

            if (TemplateContains(template, "{Time}") &&
                string.IsNullOrWhiteSpace(_selection.GetCurrentTimeString()))
            {
                message = "Не встановлено час роботи.";
                return false;
            }

            if (!HasAnySelectedDirection())
            {
                message = "Вибери напрямок польоту.";
                return false;
            }

            if (TemplateContains(template, "{ShootingTarget}") &&
                string.IsNullOrWhiteSpace(_selection.GetShootingTarget()))
            {
                message = "Вибери або введи ціль.";
                return false;
            }

            if (TemplateContains(template, "{TargetType}") &&
                string.IsNullOrWhiteSpace(_selection.GetTargetType()))
            {
                message = "Вибери тип цілі.";
                return false;
            }

            if (TemplateContains(template, "{nearestLocality}") &&
    _selection.TryGetClickedLatLng(out var clickedLatLngForLocality))
            {
                var nearestLocality =
                    _selection.FindClosestLocalityFromLatLng(clickedLatLngForLocality);

                if (string.IsNullOrWhiteSpace(nearestLocality))
                {
                    message =
                        "Найближчий населений пункт ще не визначено. Дочекайся завершення пошуку.";

                    return false;
                }
            }

            return true;
        }

        public string GenerateTextFromTemplateEditor(
    string templateName,
    List<string> template)
        {
            _selection.GetAndSetTime();

            if (!TryValidateRequiredFields(templateName, template, out var message))
            {
                ShowValidationWarning(message);
                return string.Empty;
            }

            _isSingleLineFormat = string.Equals(
                templateName,
                "Report",
                StringComparison.OrdinalIgnoreCase
            );

            return GenerateTextFromTemplate(template);
        }

        public void Start_of_Work_Click(object sender, EventArgs e)
        {
            _isSingleLineFormat = false;

            _selection.GetAndSetTime();

            if (!TryValidateRequiredFields("StartWork", _templates.StartWorkShablon, out var message))
            {
                ShowValidationWarning(message);
                return;
            }

            string generatedText = GenerateTextFromTemplate(_templates.StartWorkShablon);

            _output.SetReportText(generatedText);
            _output.CopyToClipboardWithNotification(generatedText);
        }

        public void End_of_Work_Click(object sender, EventArgs e)
        {
            _isSingleLineFormat = false;

            _selection.GetAndSetTime();

            if (!TryValidateRequiredFields("EndWork", _templates.EndWorkShablon, out var message))
            {
                ShowValidationWarning(message);
                return;
            }

            string generatedText = GenerateTextFromTemplate(_templates.EndWorkShablon);

            _output.SetReportText(generatedText);
            _output.CopyToClipboardWithNotification(generatedText);
        }

        public void Combat_Work_Click(object sender, EventArgs e)
        {
            _selection.GetAndSetTime();

            if (!TryValidateRequiredFields("Report", _templates.ReportWorkShablonActual, out var message))
            {
                ShowValidationWarning(message);
                return;
            }

            var clicked = _selection.GetClickedPoint();

            if (!clicked.HasValue)
            {
                ShowValidationWarning("Спочатку вибери точку польоту на карті.");
                return;
            }

            float azimuth = CalculateAzimuth(
                _selection.GetAttackPoint(),
                clicked.Value
            );

            _lastAzimuth = azimuth;

            _output.SetAzimuthDisplay(
                Math.Round(azimuth).ToString(CultureInfo.InvariantCulture) + "°"
            );

            _isSingleLineFormat = true;

            string generatedText = GenerateTextFromTemplate(
                _templates.ReportWorkShablonActual
            );

            _output.SetReportText(generatedText);
            _output.CopyToClipboardWithNotification(generatedText);
        }

        private bool HasFlightPoint()
        {
            return _selection.GetClickedPoint().HasValue;
        }

        private bool HasAttackPoint()
        {
            return _selection.GetAttackPoint() != PointF.Empty;
        }

        private void ShowValidationWarning(string message)
        {
            _output.SetReportText(message);
            _notification.Notify(message, NotificationType.Warning);
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