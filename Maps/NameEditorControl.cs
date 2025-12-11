// NameEditorControl.cs
using Maps.Services;
using System;
using System.Drawing.Printing;
using System.Windows.Forms;

namespace Maps
{
    public partial class NameEditorControl : UserControl
    {
        public event EventHandler BackClicked;
        public event EventHandler<string> AddPositionRequested;           // arg: newPosition
        public event EventHandler<(string position, string pilot)> AddPilotRequested;
        public event EventHandler<(string position, string drone)> AddDroneRequested;
        public event EventHandler ResetUserDataClicked;
        public event EventHandler<string> UnitNameChanged;

        private ShablonManager _shablonManager;

        public event EventHandler<List<string>> ReportTemplateChanged;
        public event EventHandler ReportTemplateReset;

        public NameEditorControl()
        {
            InitializeComponent();
        }

        public void SetShablonManager(ShablonManager shablonManager)
        {
            _shablonManager = shablonManager;
            txtLaunchArea.Text = _shablonManager?.LaunchArea ?? ""; 
            LoadCurrentTemplate();
        }

        private void LoadCurrentTemplate()
        {
            if (_shablonManager == null) return;

            var template = _shablonManager.ReportWorkShablonActual;
            txtTemplateEditor.Text = string.Join(Environment.NewLine, template);
        }

        public void SetPositions(string[] positions)
        {
            comboPosForPilot.BeginUpdate();
            comboPosForDrone.BeginUpdate();
            try
            {
                comboPosForPilot.Items.Clear();
                comboPosForDrone.Items.Clear();
                if (positions != null && positions.Length > 0)
                {
                    comboPosForPilot.Items.AddRange(positions);
                    comboPosForDrone.Items.AddRange(positions);
                    comboPosForPilot.SelectedIndex = 0;
                    comboPosForDrone.SelectedIndex = 0;
                }
            }
            finally
            {
                comboPosForPilot.EndUpdate();
                comboPosForDrone.EndUpdate();
            }
        }

        // Ця кнопка знаходиться на NameEditorControl і закриває його
        private void btnBack_Click(object sender, EventArgs e)
        {
            BackClicked?.Invoke(this, EventArgs.Empty);
        }

        private void btnAddPosition_Click(object sender, EventArgs e)
        {
            var name = (txtNewPosition.Text ?? "").Trim();
            AddPositionRequested?.Invoke(this, name);
        }

        private void btnAddPilot_Click(object sender, EventArgs e)
        {
            var pos = comboPosForPilot.SelectedItem?.ToString();
            var pilot = (txtNewPilot.Text ?? "").Trim();
            AddPilotRequested?.Invoke(this, (pos, pilot));
        }

        private void btnAddDrone_Click(object sender, EventArgs e)
        {
            var pos = comboPosForDrone.SelectedItem?.ToString();
            var drone = (txtNewDrone.Text ?? "").Trim();
            AddDroneRequested?.Invoke(this, (pos, drone));
        }

        private void btnResetUserData_Click(object sender, EventArgs e)
        {
            var result = MessageBox.Show(
                "Ця дія видалить всі ваші додані позиції, пілотів та дрони.\nПродовжити?",
                "Підтвердження",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result == DialogResult.Yes)
            {
                ResetUserDataClicked?.Invoke(this, EventArgs.Empty);
            }
        }

        private void BtnSaveUnit_Click(object sender, EventArgs e)
        {
            try
            {
                var unitName = (txtCustomUnit.Text ?? "").Trim();
                if (!string.IsNullOrEmpty(unitName))
                {
                    UnitNameChanged?.Invoke(this, unitName);
                    MessageBox.Show("Назву підрозділу збережено!", "Успіх");
                }
                else
                {
                    MessageBox.Show("Будь ласка, введіть назву підрозділу", "Попередження");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Помилка збереження: {ex.Message}", "Помилка");
            }
        }

        private void BtnSaveTemplate_Click(object sender, EventArgs e)
        {
            if (_shablonManager == null) return;

            var lines = txtTemplateEditor.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
                                   .Select(l => l.Trim())
                                   .Where(l => !string.IsNullOrEmpty(l))
                                   .ToList();

            if (lines.Count > 0)
            {
                // Додаткова перевірка
                if (lines.All(string.IsNullOrWhiteSpace))
                {
                    MessageBox.Show("Шаблон не може бути порожнім!", "Помилка",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                ReportTemplateChanged?.Invoke(this, lines);
                MessageBox.Show($"Шаблон бойової доповіді збережено! ({lines.Count} рядків)", "Успіх",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show("Шаблон не може бути порожнім!", "Помилка",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        public void SetUnitName(string unitName)
        {
            if (txtCustomUnit != null)
                txtCustomUnit.Text = unitName;
        }

        private void BtnResetTemplate_Click(object sender, EventArgs e)
        {
            var result = MessageBox.Show(
                "Скинути шаблон бойової доповіді до програмного варіанту?",
                "Підтвердження",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                ReportTemplateReset?.Invoke(this, EventArgs.Empty);
                LoadCurrentTemplate();
                MessageBox.Show("Шаблон скинуто до програмного варіанту!", "Успіх",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void BtnLoadDefault_Click(object sender, EventArgs e)
        {
            if (_shablonManager == null) return;

            // Завантажуємо програмний шаблон для перегляду
            var defaultTemplate = new System.Collections.Generic.List<string>
            {
                "{Time} БпЛА-П №1 {Position} {UnitName} 14 омбр,",
                "{nearestLocality} кв. ({MGRS_Short})",
                "виявлено БпЛА “{TargetType}” (А - {azimyth}, Д - {range}, В - {height}).",
                "Застосовано FPV дрон-перехоплювач мультироторного типу “{DroneBy}”, денний.",
                "Ціль {TargetStatus}.",
                "{Expenses}"
            };

            txtTemplateEditor.Text = string.Join(Environment.NewLine, defaultTemplate);
        }

        private void btnSaveLaunchArea_Click(object sender, EventArgs e)
        {
            var area = (txtLaunchArea.Text ?? "").Trim();
            if (string.IsNullOrEmpty(area))
            {
                MessageBox.Show("Будь ласка, введіть район зльоту", "Попередження");
                return;
            }
            if (_shablonManager != null)
            {
                _shablonManager.SaveLaunchArea(area);
                MessageBox.Show("Район зльоту збережено!", "Успіх");
            }
        }
    }
}
