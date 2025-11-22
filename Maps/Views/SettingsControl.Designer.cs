namespace Maps
{
    partial class SettingsControl
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
            btnCalibrate = new Button();
            btnSetPoint = new Button();
            btnInfo = new Button();
            btnCalculateScale = new Button();
            btnLocalities = new Button();
            btnRenameClicked = new Button();
            SuspendLayout();
            // 
            // btnCalibrate
            // 
            btnCalibrate.Location = new Point(178, 86);
            btnCalibrate.Margin = new Padding(3, 2, 3, 2);
            btnCalibrate.Name = "btnCalibrate";
            btnCalibrate.Size = new Size(175, 59);
            btnCalibrate.TabIndex = 0;
            btnCalibrate.Text = "Калібрувати карту";
            btnCalibrate.UseVisualStyleBackColor = true;
            btnCalibrate.Click += btnCalibrate_Click;
            // 
            // btnSetPoint
            // 
            btnSetPoint.Location = new Point(178, 232);
            btnSetPoint.Margin = new Padding(3, 2, 3, 2);
            btnSetPoint.Name = "btnSetPoint";
            btnSetPoint.Size = new Size(175, 59);
            btnSetPoint.TabIndex = 2;
            btnSetPoint.Text = "Виставити позицію";
            btnSetPoint.UseVisualStyleBackColor = true;
            btnSetPoint.Click += btnSetPoint_Click;
            // 
            // btnInfo
            // 
            btnInfo.Location = new Point(209, 626);
            btnInfo.Margin = new Padding(3, 2, 3, 2);
            btnInfo.Name = "btnInfo";
            btnInfo.Size = new Size(112, 27);
            btnInfo.TabIndex = 3;
            btnInfo.Text = "Статистика польотів";
            btnInfo.UseVisualStyleBackColor = true;
            btnInfo.Click += btnInfo_Click;
            // 
            // btnCalculateScale
            // 
            btnCalculateScale.Location = new Point(178, 159);
            btnCalculateScale.Margin = new Padding(3, 2, 3, 2);
            btnCalculateScale.Name = "btnCalculateScale";
            btnCalculateScale.Size = new Size(175, 59);
            btnCalculateScale.TabIndex = 4;
            btnCalculateScale.Text = "калібрувати відстань карти";
            btnCalculateScale.UseVisualStyleBackColor = true;
            btnCalculateScale.Click += btnCalculateScale_Click;
            // 
            // btnLocalities
            // 
            btnLocalities.Location = new Point(178, 307);
            btnLocalities.Margin = new Padding(3, 2, 3, 2);
            btnLocalities.Name = "btnLocalities";
            btnLocalities.Size = new Size(175, 59);
            btnLocalities.TabIndex = 5;
            btnLocalities.Text = "Населені пункти";
            btnLocalities.UseVisualStyleBackColor = true;
            btnLocalities.Click += btnLocalities_Click;
            // 
            // button1
            // 
            btnRenameClicked.Location = new Point(178, 382);
            btnRenameClicked.Name = "btnRenameClicked";
            btnRenameClicked.Size = new Size(175, 59);
            btnRenameClicked.TabIndex = 6;
            btnRenameClicked.Text = "Налаштування Позиції";
            btnRenameClicked.UseVisualStyleBackColor = true;
            btnRenameClicked.Click += btnRename_Click;
            // 
            // SettingsControl
            // 
            AutoScaleDimensions = new SizeF(10F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(btnRenameClicked);
            Controls.Add(btnLocalities);
            Controls.Add(btnCalculateScale);
            Controls.Add(btnInfo);
            Controls.Add(btnSetPoint);
            Controls.Add(btnCalibrate);
            Margin = new Padding(3, 2, 3, 2);
            Name = "SettingsControl";
            Size = new Size(553, 724);
            ResumeLayout(false);
        }

        #endregion

        private Button btnCalibrate;
        private Button btnSetPoint;
        private Button btnInfo;
        private Button btnCalculateScale;
        private Button btnLocalities;
        private Button btnRenameClicked;
    }
}
