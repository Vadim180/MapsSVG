using System.Windows.Forms;

namespace Maps
{
    partial class Maps
    {
        private System.ComponentModel.IContainer components = null;



        private Panel panelMap;
        private Panel panelRight;
        private Panel panelOptions;
        private Panel panelSettings;
        private Panel panelSettingsView;
        private Panel panelMainView;

        /// <summary>
        /// Освободить все используемые ресурсы.
        /// </summary>
        /// <param name="disposing">истинно, якщо управляемый ресурс должен быть удален; иначе ложно.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (components != null)
                {
                    components.Dispose();
                }

                if (timer != null)
                {
                    timer.Dispose();
                    //timer = null;
                }
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            panelMap = new Panel();
            labelScale_serva = new Label();
            labelCoordinates = new Label();
            labelScale = new Label();
            pictureBox1 = new PictureBox();
            panelRight = new Panel();
            panelOptions = new Panel();
            panelMainView = new Panel();
            targetBoardCheckBox = new CheckBox();
            targetDestroyedCheckBox = new CheckBox();
            Target_Type = new ComboBox();
            label_course_Value = new Label();
            label_Course = new Label();
            label_range_Value = new Label();
            range2 = new Label();
            label_height = new Label();
            label_shootingTarget = new Label();
            label_DroneBy = new Label();
            label_Pilot = new Label();
            label_Position = new Label();
            textBox1 = new TextBox();
            Combat_Work = new Button();
            End_of_Work = new Button();
            Start_of_Work = new Button();
            height = new TextBox();
            shootingTarget = new TextBox();
            flowLayoutLocalities = new FlowLayoutPanel();
            DroneBy = new ComboBox();
            Pilot = new ComboBox();
            Position = new ComboBox();
            panelSettingsView = new Panel();
            panelSettings = new Panel();
            btnHome = new Button();
            btnSettingsScreen = new Button();
            timer = new System.Windows.Forms.Timer(components);
            toolTip1 = new ToolTip(components);
            label2 = new Label();
            panelMap.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)pictureBox1).BeginInit();
            panelRight.SuspendLayout();
            panelOptions.SuspendLayout();
            panelMainView.SuspendLayout();
            panelSettings.SuspendLayout();
            SuspendLayout();
            // 
            // panelMap
            // 
            panelMap.Controls.Add(labelScale_serva);
            panelMap.Controls.Add(labelCoordinates);
            panelMap.Controls.Add(labelScale);
            panelMap.Controls.Add(pictureBox1);
            panelMap.Dock = DockStyle.Fill;
            panelMap.Location = new Point(0, 0);
            panelMap.Margin = new Padding(0, 6, 0, 6);
            panelMap.Name = "panelMap";
            panelMap.Size = new Size(1412, 1061);
            panelMap.TabIndex = 0;
            // 
            // labelScale_serva
            // 
            labelScale_serva.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            labelScale_serva.AutoSize = true;
            labelScale_serva.BackColor = Color.Black;
            labelScale_serva.Font = new Font("Arial", 16F, FontStyle.Bold);
            labelScale_serva.ForeColor = Color.White;
            labelScale_serva.Location = new Point(1201, 100);
            labelScale_serva.Margin = new Padding(0);
            labelScale_serva.Name = "labelScale_serva";
            labelScale_serva.Size = new Size(179, 37);
            labelScale_serva.TabIndex = 3;
            labelScale_serva.Text = "СЕРВА: 40";
            labelScale_serva.TextAlign = ContentAlignment.MiddleRight;
            // 
            // labelCoordinates
            // 
            labelCoordinates.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            labelCoordinates.AutoSize = true;
            labelCoordinates.BackColor = Color.FromArgb(30, 30, 30);
            labelCoordinates.BorderStyle = BorderStyle.FixedSingle;
            labelCoordinates.Font = new Font("Consolas", 10F);
            labelCoordinates.ForeColor = Color.White;
            labelCoordinates.Location = new Point(10, 10);
            labelCoordinates.Name = "labelCoordinates";
            labelCoordinates.Padding = new Padding(5);
            labelCoordinates.Size = new Size(200, 100);
            labelCoordinates.TabIndex = 0;
            labelCoordinates.Text = "Відкалібруйте карту";
            // 
            // labelScale
            // 
            labelScale.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            labelScale.AutoSize = true;
            labelScale.BackColor = Color.Black;
            labelScale.Font = new Font("Arial", 16F, FontStyle.Bold);
            labelScale.ForeColor = Color.White;
            labelScale.Location = new Point(1201, 9);
            labelScale.Name = "labelScale";
            labelScale.Size = new Size(118, 37);
            labelScale.TabIndex = 2;
            labelScale.Text = "Кут: 0°";
            labelScale.TextAlign = ContentAlignment.MiddleRight;
            // 
            // pictureBox1 - видалено, використовується mapControl (GMap.NET)
            // 
            // 
            // panelRight
            // 
            panelRight.Controls.Add(panelOptions);
            panelRight.Controls.Add(panelSettings);
            panelRight.Dock = DockStyle.Right;
            panelRight.Location = new Point(1412, 0);
            panelRight.Margin = new Padding(0, 6, 0, 6);
            panelRight.Name = "panelRight";
            panelRight.Size = new Size(512, 1061);
            panelRight.TabIndex = 1;
            // 
            // panelOptions
            // 
            panelOptions.Controls.Add(panelMainView);
            panelOptions.Controls.Add(panelSettingsView);
            panelOptions.Dock = DockStyle.Fill;
            panelOptions.Location = new Point(0, 100);
            panelOptions.Name = "panelOptions";
            panelOptions.Size = new Size(512, 961);
            panelOptions.TabIndex = 0;
            // 
            // panelMainView
            // 
            panelMainView.AutoScroll = true;
            panelMainView.BackColor = Color.Gray;
            panelMainView.Controls.Add(label2);
            panelMainView.Controls.Add(targetBoardCheckBox);
            panelMainView.Controls.Add(targetDestroyedCheckBox);
            panelMainView.Controls.Add(Target_Type);
            panelMainView.Controls.Add(label_course_Value);
            panelMainView.Controls.Add(label_Course);
            panelMainView.Controls.Add(label_range_Value);
            panelMainView.Controls.Add(range2);
            panelMainView.Controls.Add(label_height);
            panelMainView.Controls.Add(label_shootingTarget);
            panelMainView.Controls.Add(label_DroneBy);
            panelMainView.Controls.Add(label_Pilot);
            panelMainView.Controls.Add(label_Position);
            panelMainView.Controls.Add(textBox1);
            panelMainView.Controls.Add(Combat_Work);
            panelMainView.Controls.Add(End_of_Work);
            panelMainView.Controls.Add(Start_of_Work);
            panelMainView.Controls.Add(height);
            panelMainView.Controls.Add(shootingTarget);
            panelMainView.Controls.Add(flowLayoutLocalities);
            panelMainView.Controls.Add(DroneBy);
            panelMainView.Controls.Add(Pilot);
            panelMainView.Controls.Add(Position);
            panelMainView.Dock = DockStyle.Fill;
            panelMainView.Location = new Point(0, 0);
            panelMainView.Name = "panelMainView";
            panelMainView.Size = new Size(512, 961);
            panelMainView.TabIndex = 0;
            // 
            // targetBoardCheckBox
            // 
            targetBoardCheckBox.AutoSize = true;
            targetBoardCheckBox.CheckAlign = ContentAlignment.MiddleRight;
            targetBoardCheckBox.Location = new Point(337, 590);
            targetBoardCheckBox.Name = "targetBoardCheckBox";
            targetBoardCheckBox.Size = new Size(162, 24);
            targetBoardCheckBox.TabIndex = 27;
            targetBoardCheckBox.Text = "Борт втрачено";
            targetBoardCheckBox.UseVisualStyleBackColor = true;
            // 
            // targetDestroyedCheckBox
            // 
            targetDestroyedCheckBox.AutoSize = true;
            targetDestroyedCheckBox.CheckAlign = ContentAlignment.MiddleRight;
            targetDestroyedCheckBox.Font = new Font("Segoe UI", 9F);
            targetDestroyedCheckBox.ForeColor = SystemColors.ControlText;
            targetDestroyedCheckBox.Location = new Point(182, 587);
            targetDestroyedCheckBox.Name = "targetDestroyedCheckBox";
            targetDestroyedCheckBox.Size = new Size(151, 29);
            targetDestroyedCheckBox.TabIndex = 26;
            targetDestroyedCheckBox.Text = "Ціль знищено";
            targetDestroyedCheckBox.UseVisualStyleBackColor = true;
            // 
            // Target_Type
            // 
            Target_Type.FormattingEnabled = true;
            Target_Type.Location = new Point(10, 588);
            Target_Type.Name = "Target_Type";
            Target_Type.Size = new Size(169, 28);
            Target_Type.TabIndex = 25;
            Target_Type.TabStop = false;
            // 
            // label_course_Value
            // 
            label_course_Value.AutoSize = true;
            label_course_Value.Location = new Point(392, 477);
            label_course_Value.Name = "label_course_Value";
            label_course_Value.Size = new Size(15, 20);
            label_course_Value.TabIndex = 24;
            label_course_Value.Text = "-";
            // 
            // label_Course
            // 
            label_Course.AutoSize = true;
            label_Course.Location = new Point(374, 443);
            label_Course.Name = "label_Course";
            label_Course.Size = new Size(47, 20);
            label_Course.TabIndex = 23;
            label_Course.Text = "Курс";
            // 
            // label_range_Value
            // 
            label_range_Value.AutoSize = true;
            label_range_Value.Location = new Point(69, 477);
            label_range_Value.Name = "label_range_Value";
            label_range_Value.Size = new Size(18, 20);
            label_range_Value.TabIndex = 22;
            label_range_Value.Text = "0";
            // 
            // range2
            // 
            range2.AutoSize = true;
            range2.Location = new Point(35, 443);
            range2.Margin = new Padding(0);
            range2.Name = "range2";
            range2.Size = new Size(105, 20);
            range2.TabIndex = 21;
            range2.Text = "Дистанція: ";
            // 
            // label_height
            // 
            label_height.AutoSize = true;
            label_height.Location = new Point(214, 443);
            label_height.Margin = new Padding(0);
            label_height.Name = "label_height";
            label_height.Size = new Size(70, 20);
            label_height.TabIndex = 20;
            label_height.Text = "Висота";
            // 
            // label_shootingTarget
            // 
            label_shootingTarget.AutoSize = true;
            label_shootingTarget.Font = new Font("Segoe UI", 12F);
            label_shootingTarget.Location = new Point(109, 148);
            label_shootingTarget.Margin = new Padding(0);
            label_shootingTarget.Name = "label_shootingTarget";
            label_shootingTarget.Size = new Size(97, 32);
            label_shootingTarget.TabIndex = 18;
            label_shootingTarget.Text = "Ціль №";
            // 
            // label_DroneBy
            // 
            label_DroneBy.AutoSize = true;
            label_DroneBy.Font = new Font("Segoe UI", 13F, FontStyle.Bold);
            label_DroneBy.Location = new Point(123, 97);
            label_DroneBy.Margin = new Padding(0);
            label_DroneBy.Name = "label_DroneBy";
            label_DroneBy.Size = new Size(83, 36);
            label_DroneBy.TabIndex = 17;
            label_DroneBy.Text = "Дрон";
            // 
            // label_Pilot
            // 
            label_Pilot.AutoSize = true;
            label_Pilot.Font = new Font("Segoe UI", 13F, FontStyle.Bold);
            label_Pilot.Location = new Point(123, 52);
            label_Pilot.Margin = new Padding(0);
            label_Pilot.Name = "label_Pilot";
            label_Pilot.Size = new Size(86, 36);
            label_Pilot.TabIndex = 16;
            label_Pilot.Text = "Пілот";
            // 
            // label_Position
            // 
            label_Position.AutoSize = true;
            label_Position.Font = new Font("Segoe UI", 13F, FontStyle.Bold);
            label_Position.Location = new Point(90, 8);
            label_Position.Margin = new Padding(0);
            label_Position.Name = "label_Position";
            label_Position.Size = new Size(119, 36);
            label_Position.TabIndex = 15;
            label_Position.Text = "Позиція";
            // 
            // textBox1
            // 
            textBox1.Location = new Point(11, 696);
            textBox1.Margin = new Padding(0, 6, 0, 6);
            textBox1.Multiline = true;
            textBox1.Name = "textBox1";
            textBox1.Size = new Size(474, 407);
            textBox1.TabIndex = 14;
            // 
            // Combat_Work
            // 
            Combat_Work.Location = new Point(328, 642);
            Combat_Work.Margin = new Padding(0, 6, 0, 6);
            Combat_Work.Name = "Combat_Work";
            Combat_Work.Size = new Size(138, 42);
            Combat_Work.TabIndex = 13;
            Combat_Work.Text = "Бойова";
            Combat_Work.UseVisualStyleBackColor = true;
            Combat_Work.Click += Combat_Work_Click;
            // 
            // End_of_Work
            // 
            End_of_Work.Location = new Point(166, 642);
            End_of_Work.Margin = new Padding(0, 6, 0, 6);
            End_of_Work.Name = "End_of_Work";
            End_of_Work.Size = new Size(138, 42);
            End_of_Work.TabIndex = 12;
            End_of_Work.Text = "Закінчення";
            End_of_Work.UseVisualStyleBackColor = true;
            End_of_Work.Click += End_of_Work_Click;
            // 
            // Start_of_Work
            // 
            Start_of_Work.Location = new Point(10, 642);
            Start_of_Work.Margin = new Padding(0, 6, 0, 6);
            Start_of_Work.Name = "Start_of_Work";
            Start_of_Work.Size = new Size(138, 42);
            Start_of_Work.TabIndex = 11;
            Start_of_Work.Text = "Згенерувати";
            Start_of_Work.UseVisualStyleBackColor = true;
            Start_of_Work.Click += Start_of_Work_Click;
            // 
            // height
            // 
            height.Location = new Point(189, 474);
            height.Margin = new Padding(0, 6, 0, 6);
            height.Multiline = true;
            height.Name = "height";
            height.Size = new Size(119, 36);
            height.TabIndex = 10;
            // 
            // shootingTarget
            // 
            shootingTarget.ForeColor = SystemColors.GrayText;
            shootingTarget.Location = new Point(227, 144);
            shootingTarget.Margin = new Padding(0, 6, 0, 6);
            shootingTarget.Multiline = true;
            shootingTarget.Name = "shootingTarget";
            shootingTarget.PlaceholderText = "Патрулювання";
            shootingTarget.Size = new Size(177, 36);
            shootingTarget.TabIndex = 7;
            shootingTarget.Enter += shootingTarget_Enter;
            shootingTarget.Leave += shootingTarget_Leave;
            // 
            // flowLayoutLocalities
            // 
            flowLayoutLocalities.Location = new Point(3, 192);
            flowLayoutLocalities.Margin = new Padding(0, 6, 0, 6);
            flowLayoutLocalities.Name = "flowLayoutLocalities";
            flowLayoutLocalities.Size = new Size(482, 242);
            flowLayoutLocalities.TabIndex = 6;
            // 
            // DroneBy
            // 
            DroneBy.DropDownStyle = ComboBoxStyle.DropDownList;
            DroneBy.FormattingEnabled = true;
            DroneBy.Location = new Point(227, 100);
            DroneBy.Margin = new Padding(0, 6, 0, 6);
            DroneBy.Name = "DroneBy";
            DroneBy.Size = new Size(177, 28);
            DroneBy.TabIndex = 5;
            DroneBy.TabStop = false;
            // 
            // Pilot
            // 
            Pilot.DropDownStyle = ComboBoxStyle.DropDownList;
            Pilot.FormattingEnabled = true;
            Pilot.Location = new Point(227, 55);
            Pilot.Margin = new Padding(0, 6, 0, 6);
            Pilot.Name = "Pilot";
            Pilot.Size = new Size(177, 28);
            Pilot.TabIndex = 4;
            Pilot.TabStop = false;
            // 
            // Position
            // 
            Position.DropDownStyle = ComboBoxStyle.DropDownList;
            Position.FormattingEnabled = true;
            Position.Location = new Point(227, 14);
            Position.Margin = new Padding(0, 6, 0, 6);
            Position.Name = "Position";
            Position.Size = new Size(177, 28);
            Position.TabIndex = 3;
            Position.TabStop = false;
            Position.SelectedIndexChanged += ComboBoxPilot_SelectedIndexChanged;
            // 
            // panelSettingsView
            // 
            panelSettingsView.BackColor = Color.LightGray;
            panelSettingsView.Dock = DockStyle.Fill;
            panelSettingsView.Location = new Point(0, 0);
            panelSettingsView.Name = "panelSettingsView";
            panelSettingsView.Size = new Size(512, 961);
            panelSettingsView.TabIndex = 1;
            panelSettingsView.Visible = false;
            // 
            // panelSettings
            // 
            panelSettings.BackColor = Color.Gray;
            panelSettings.Controls.Add(btnHome);
            panelSettings.Controls.Add(btnSettingsScreen);
            panelSettings.Dock = DockStyle.Top;
            panelSettings.Location = new Point(0, 0);
            panelSettings.Name = "panelSettings";
            panelSettings.Size = new Size(512, 100);
            panelSettings.TabIndex = 1;
            // 
            // btnHome
            // 
            btnHome.BackColor = Color.White;
            btnHome.Enabled = false;
            btnHome.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            btnHome.Location = new Point(46, 16);
            btnHome.Name = "btnHome";
            btnHome.Size = new Size(182, 55);
            btnHome.TabIndex = 0;
            btnHome.Text = "Головна";
            btnHome.UseVisualStyleBackColor = false;
            btnHome.Click += BtnHome_Click;
            // 
            // btnSettingsScreen
            // 
            btnSettingsScreen.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            btnSettingsScreen.Location = new Point(269, 16);
            btnSettingsScreen.Name = "btnSettingsScreen";
            btnSettingsScreen.Size = new Size(184, 55);
            btnSettingsScreen.TabIndex = 1;
            btnSettingsScreen.Text = "Налаштування";
            btnSettingsScreen.UseVisualStyleBackColor = true;
            btnSettingsScreen.Click += BtnSettingsScreen_Click;
            // 
            // timer
            // 
            timer.Interval = 10;
            timer.Tick += Timer_Tick;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(73, 540);
            label2.Name = "label2";
            label2.Size = new Size(53, 20);
            label2.TabIndex = 28;
            label2.Text = "label2";
            // 
            // Maps
            // 
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(1924, 1061);
            Controls.Add(panelMap);
            Controls.Add(panelRight);
            KeyPreview = true;
            Margin = new Padding(0, 6, 0, 6);
            Name = "Maps";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Maps";
            WindowState = FormWindowState.Maximized;
            FormClosed += Maps_FormClosed;
            Load += Maps_Load;
            KeyDown += Maps_KeyDown;
            KeyUp += Maps_KeyUp;
            MouseWheel += Maps_MouseWheel;
            Resize += Maps_Resize;
            panelMap.ResumeLayout(false);
            panelMap.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)pictureBox1).EndInit();
            panelRight.ResumeLayout(false);
            panelOptions.ResumeLayout(false);
            panelMainView.ResumeLayout(false);
            panelMainView.PerformLayout();
            panelSettings.ResumeLayout(false);
            panelSettings.PerformLayout();
            ResumeLayout(false);
        }


        #endregion

        private Label label_Position;
        private ComboBox Position;
        private Label label_Pilot;
        private ComboBox Pilot;
        private Label label_DroneBy;
        private ComboBox DroneBy;
        private FlowLayoutPanel flowLayoutLocalities;
        private Label range2;
        private Label label_range_Value;
        private Label label_height;
        private TextBox height;
        private TextBox shootingTarget;
        private Button End_of_Work;
        private Label label_shootingTarget;
        private Button Start_of_Work;
        private Button Combat_Work;
        private TextBox textBox1;
        private Label labelScale;
        private Label labelCoordinates;
        private Label labelScale_serva;
        private Button btnHome;
        private Button btnSettingsScreen;
        private Label label_Course;
        private Label label_course_Value;
        private ComboBox Target_Type;
        private CheckBox targetDestroyedCheckBox;
        private CheckBox targetBoardCheckBox;
        private PictureBox pictureBox1;
        private ToolTip toolTip1;
        private Label label2;
    }
}