namespace Maps
{
    partial class FlightStatsForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            textBoxStats = new TextBox();
            SuspendLayout();
            // 
            // textBoxStats
            // 
            textBoxStats.Dock = DockStyle.Fill;
            textBoxStats.Location = new Point(0, 0);
            textBoxStats.Multiline = true;
            textBoxStats.Name = "textBoxStats";
            textBoxStats.ReadOnly = true;
            textBoxStats.ScrollBars = ScrollBars.Vertical;
            textBoxStats.Size = new Size(800, 450);
            textBoxStats.TabIndex = 0;
            // 
            // FlightStatsForm
            // 
            AutoScaleDimensions = new SizeF(10F, 25F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(textBoxStats);
            Name = "FlightStatsForm";
            Text = "FlightStatsForm";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private TextBox textBoxStats;
    }
}