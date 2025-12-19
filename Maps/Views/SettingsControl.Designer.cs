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
            btnInfo = new Button();
            btnRenameClicked = new Button();
            SuspendLayout();
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
            Controls.Add(btnInfo);
            Margin = new Padding(3, 2, 3, 2);
            Name = "SettingsControl";
            Size = new Size(553, 724);
            ResumeLayout(false);
        }

        #endregion

        private Button btnInfo;
        private Button btnRenameClicked;
    }
}
