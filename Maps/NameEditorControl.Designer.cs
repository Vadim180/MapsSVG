namespace Maps
{
    partial class NameEditorControl
    {
        /// <summary> 
        /// Обязательная переменная конструктора.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Освободить все используемые ресурсы.
        /// </summary>
        /// <param name="disposing">истинно, если управляемый ресурс должен быть удален; иначе ложно.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Код, автоматически созданный конструктором компонентов

        /// <summary> 
        /// Требуемый метод для поддержки конструктора — не изменяйте 
        /// содержимое этого метода с помощью редактора кода.
        /// </summary>
        private void InitializeComponent()
        {
            txtNewPosition = new TextBox();
            btnAddPosition = new Button();
            comboPosForPilot = new ComboBox();
            txtNewPilot = new TextBox();
            btnAddPilot = new Button();
            comboPosForDrone = new ComboBox();
            txtNewDrone = new TextBox();
            btnAddDrone = new Button();
            btnBack = new Button();
            btnResetUserData = new Button();
            txtTemplateEditor = new TextBox();
            btnSaveTemplate = new Button();
            btnResetTemplate = new Button();
            txtCustomUnit = new TextBox();
            btnSaveUnit = new Button();
            label1 = new Label();
            txtLaunchArea = new TextBox();
            btnSaveLaunchArea = new Button();
            SuspendLayout();
            // 
            // txtNewPosition
            // 
            txtNewPosition.Location = new Point(193, 2);
            txtNewPosition.Multiline = true;
            txtNewPosition.Name = "txtNewPosition";
            txtNewPosition.Size = new Size(179, 44);
            txtNewPosition.TabIndex = 0;
            // 
            // btnAddPosition
            // 
            btnAddPosition.Location = new Point(193, 52);
            btnAddPosition.Name = "btnAddPosition";
            btnAddPosition.Size = new Size(179, 56);
            btnAddPosition.TabIndex = 1;
            btnAddPosition.Text = "Додати Позицію";
            btnAddPosition.UseVisualStyleBackColor = true;
            btnAddPosition.Click += btnAddPosition_Click;
            // 
            // comboPosForPilot
            // 
            comboPosForPilot.DropDownStyle = ComboBoxStyle.DropDownList;
            comboPosForPilot.Font = new Font("Microsoft Sans Serif", 12.25F);
            comboPosForPilot.FormattingEnabled = true;
            comboPosForPilot.Location = new Point(193, 120);
            comboPosForPilot.Name = "comboPosForPilot";
            comboPosForPilot.Size = new Size(179, 37);
            comboPosForPilot.TabIndex = 2;
            // 
            // txtNewPilot
            // 
            txtNewPilot.Location = new Point(193, 163);
            txtNewPilot.Multiline = true;
            txtNewPilot.Name = "txtNewPilot";
            txtNewPilot.Size = new Size(179, 40);
            txtNewPilot.TabIndex = 3;
            // 
            // btnAddPilot
            // 
            btnAddPilot.Location = new Point(193, 209);
            btnAddPilot.Name = "btnAddPilot";
            btnAddPilot.Size = new Size(179, 42);
            btnAddPilot.TabIndex = 4;
            btnAddPilot.Text = "Додати Пілота";
            btnAddPilot.UseVisualStyleBackColor = true;
            btnAddPilot.Click += btnAddPilot_Click;
            // 
            // comboPosForDrone
            // 
            comboPosForDrone.DropDownStyle = ComboBoxStyle.DropDownList;
            comboPosForDrone.Font = new Font("Microsoft Sans Serif", 12.25F);
            comboPosForDrone.FormattingEnabled = true;
            comboPosForDrone.Location = new Point(193, 263);
            comboPosForDrone.Name = "comboPosForDrone";
            comboPosForDrone.Size = new Size(179, 37);
            comboPosForDrone.TabIndex = 5;
            // 
            // txtNewDrone
            // 
            txtNewDrone.Location = new Point(193, 306);
            txtNewDrone.Multiline = true;
            txtNewDrone.Name = "txtNewDrone";
            txtNewDrone.Size = new Size(179, 44);
            txtNewDrone.TabIndex = 6;
            // 
            // btnAddDrone
            // 
            btnAddDrone.Location = new Point(193, 356);
            btnAddDrone.Name = "btnAddDrone";
            btnAddDrone.Size = new Size(179, 55);
            btnAddDrone.TabIndex = 7;
            btnAddDrone.Text = "Додати Дрон";
            btnAddDrone.UseVisualStyleBackColor = true;
            btnAddDrone.Click += btnAddDrone_Click;
            // 
            // btnBack
            // 
            btnBack.Location = new Point(206, 1091);
            btnBack.Name = "btnBack";
            btnBack.Size = new Size(179, 59);
            btnBack.TabIndex = 8;
            btnBack.Text = "Назад";
            btnBack.UseVisualStyleBackColor = true;
            btnBack.Click += btnBack_Click;
            // 
            // btnResetUserData
            // 
            btnResetUserData.Location = new Point(163, 417);
            btnResetUserData.Name = "btnResetUserData";
            btnResetUserData.Size = new Size(233, 49);
            btnResetUserData.TabIndex = 9;
            btnResetUserData.Text = "Скинути налаштування";
            btnResetUserData.UseVisualStyleBackColor = true;
            btnResetUserData.Click += btnResetUserData_Click;
            // 
            // txtTemplateEditor
            // 
            txtTemplateEditor.Location = new Point(16, 778);
            txtTemplateEditor.Multiline = true;
            txtTemplateEditor.Name = "txtTemplateEditor";
            txtTemplateEditor.Size = new Size(545, 230);
            txtTemplateEditor.TabIndex = 10;
            // 
            // btnSaveTemplate
            // 
            btnSaveTemplate.Location = new Point(185, 1014);
            btnSaveTemplate.Name = "btnSaveTemplate";
            btnSaveTemplate.Size = new Size(203, 31);
            btnSaveTemplate.TabIndex = 11;
            btnSaveTemplate.Text = "Зберегти ";
            btnSaveTemplate.UseVisualStyleBackColor = true;
            btnSaveTemplate.Click += BtnSaveTemplate_Click;
            // 
            // btnResetTemplate
            // 
            btnResetTemplate.Location = new Point(185, 1051);
            btnResetTemplate.Name = "btnResetTemplate";
            btnResetTemplate.Size = new Size(203, 31);
            btnResetTemplate.TabIndex = 12;
            btnResetTemplate.Text = "Програмний шаблон";
            btnResetTemplate.UseVisualStyleBackColor = true;
            btnResetTemplate.Click += BtnResetTemplate_Click;
            // 
            // txtCustomUnit
            // 
            txtCustomUnit.Location = new Point(204, 472);
            txtCustomUnit.Name = "txtCustomUnit";
            txtCustomUnit.Size = new Size(150, 26);
            txtCustomUnit.TabIndex = 15;
            // 
            // btnSaveUnit
            // 
            btnSaveUnit.Location = new Point(220, 504);
            btnSaveUnit.Name = "btnSaveUnit";
            btnSaveUnit.Size = new Size(112, 34);
            btnSaveUnit.TabIndex = 16;
            btnSaveUnit.Text = "Зберегти підрозділ";
            btnSaveUnit.UseVisualStyleBackColor = true;
            btnSaveUnit.Click += BtnSaveUnit_Click;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(212, 552);
            label1.Name = "label1";
            label1.Size = new Size(122, 20);
            label1.TabIndex = 17;
            label1.Text = "Район зльоту";
            // 
            // txtLaunchArea
            // 
            txtLaunchArea.Location = new Point(193, 586);
            txtLaunchArea.Name = "txtLaunchArea";
            txtLaunchArea.Size = new Size(150, 26);
            txtLaunchArea.TabIndex = 18;
            // 
            // btnSaveLaunchArea
            // 
            btnSaveLaunchArea.Location = new Point(185, 628);
            btnSaveLaunchArea.Name = "btnSaveLaunchArea";
            btnSaveLaunchArea.Size = new Size(161, 53);
            btnSaveLaunchArea.TabIndex = 19;
            btnSaveLaunchArea.Text = "Зберегти район зльоту";
            btnSaveLaunchArea.UseVisualStyleBackColor = true;
            btnSaveLaunchArea.Click += btnSaveLaunchArea_Click;
            // 
            // NameEditorControl
            // 
            AutoScaleDimensions = new SizeF(10F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(btnSaveLaunchArea);
            Controls.Add(txtLaunchArea);
            Controls.Add(label1);
            Controls.Add(btnSaveUnit);
            Controls.Add(txtCustomUnit);
            Controls.Add(btnResetTemplate);
            Controls.Add(btnSaveTemplate);
            Controls.Add(txtTemplateEditor);
            Controls.Add(btnResetUserData);
            Controls.Add(btnBack);
            Controls.Add(btnAddDrone);
            Controls.Add(txtNewDrone);
            Controls.Add(comboPosForDrone);
            Controls.Add(btnAddPilot);
            Controls.Add(txtNewPilot);
            Controls.Add(comboPosForPilot);
            Controls.Add(btnAddPosition);
            Controls.Add(txtNewPosition);
            Name = "NameEditorControl";
            Size = new Size(599, 1183);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private TextBox txtNewPosition;
        private Button btnAddPosition;
        private ComboBox comboPosForPilot;
        private TextBox txtNewPilot;
        private Button btnAddPilot;
        private ComboBox comboPosForDrone;
        private TextBox txtNewDrone;
        private Button btnAddDrone;
        private Button btnBack;
        private Button btnResetUserData;
        private TextBox txtTemplateEditor;
        private Button btnSaveTemplate;
        private Button btnResetTemplate;
        private TextBox txtCustomUnit;
        private Button btnSaveUnit;
        private Label label1;
        private TextBox txtLaunchArea;
        private Button btnSaveLaunchArea;
    }
}
