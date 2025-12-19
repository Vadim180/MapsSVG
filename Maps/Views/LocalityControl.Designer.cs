using System.Security.Policy;

namespace Maps.Views
{
    partial class LocalityControl
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
            listBoxLocalities = new ListBox();
            exit = new Button();
            SuspendLayout();
            // 
            // listBoxLocalities
            // 
            listBoxLocalities.FormattingEnabled = true;
            listBoxLocalities.ItemHeight = 25;
            listBoxLocalities.Location = new Point(0, 0);
            listBoxLocalities.Name = "listBoxLocalities";
            listBoxLocalities.SelectionMode = SelectionMode.MultiExtended;
            listBoxLocalities.Size = new Size(595, 804);
            listBoxLocalities.TabIndex = 0;
            listBoxLocalities.SelectedIndexChanged += listBoxLocalities_SelectedIndexChanged;
            // 
            // button1
            // 
            exit.Location = new Point(209, 810);
            exit.Name = "exit";
            exit.Size = new Size(172, 48);
            exit.TabIndex = 1;
            exit.Text = "Назад";
            exit.UseVisualStyleBackColor = true;
            exit.Click += exit_Click;
            // 
            // LocalityControl
            // 
            AutoScaleDimensions = new SizeF(10F, 25F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(exit);
            Controls.Add(listBoxLocalities);
            Name = "LocalityControl";
            Size = new Size(595, 876);
            ResumeLayout(false);
        }

        #endregion

        private ListBox listBoxLocalities;
        private Button exit;
    }
}
