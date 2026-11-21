namespace ApprovalAdministrationTool
{
    partial class ApprovalAdminControl
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

        #region Code généré par le Concepteur de composants

        /// <summary>
        /// Method required for designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.toolStripSearch = new System.Windows.Forms.ToolStrip();
            this.tslSearchIcon = new System.Windows.Forms.ToolStripLabel();
            this.tstSearch = new System.Windows.Forms.ToolStripTextBox();
            this.searchDelay = new System.Windows.Forms.Timer(this.components);
            this.toolStripMenu = new System.Windows.Forms.ToolStrip();
            this.tsbHeaderNames = new System.Windows.Forms.ToolStripButton();
            this.tssSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.buttonResetSettings = new System.Windows.Forms.ToolStripButton();
            this.tssSeparator2 = new System.Windows.Forms.ToolStripSeparator();
            this.tsbModifyQuery = new System.Windows.Forms.ToolStripButton();
            this.queryEditor = new ApprovalAdministrationTool.Controls.QueryEditorControl();
            this.splitterQuery = new System.Windows.Forms.Splitter();
            this.tsbRefresh = new System.Windows.Forms.ToolStripButton();
            this.splitContainer1 = new System.Windows.Forms.SplitContainer();
            this.dataGridControl1 = new ApprovalAdministrationTool.Controls.DataGridControl();
            this.webContentControl1 = new ApprovalAdministrationTool.Controls.WebContentControl();
            this.toolStripSearch.SuspendLayout();
            this.toolStripMenu.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).BeginInit();
            this.splitContainer1.Panel1.SuspendLayout();
            this.splitContainer1.Panel2.SuspendLayout();
            this.splitContainer1.SuspendLayout();
            this.SuspendLayout();
            //
            // toolStripMenu
            //
            this.toolStripMenu.ImageScalingSize = new System.Drawing.Size(24, 24);
            this.toolStripMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tsbHeaderNames,
            this.tssSeparator1,
            this.buttonResetSettings,
            this.tssSeparator2,
            this.tsbModifyQuery,
            this.tsbRefresh});
            this.toolStripMenu.Location = new System.Drawing.Point(0, 0);
            this.toolStripMenu.Name = "toolStripMenu";
            this.toolStripMenu.Size = new System.Drawing.Size(630, 25);
            this.toolStripMenu.TabIndex = 4;
            this.toolStripMenu.Text = "toolStrip1";
            //
            // tssSeparator2
            //
            this.tssSeparator2.Name = "tssSeparator2";
            this.tssSeparator2.Size = new System.Drawing.Size(6, 25);
            //
            // tsbModifyQuery
            //
            this.tsbModifyQuery.CheckOnClick = true;
            this.tsbModifyQuery.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsbModifyQuery.Name = "tsbModifyQuery";
            this.tsbModifyQuery.Size = new System.Drawing.Size(80, 22);
            this.tsbModifyQuery.Text = "Modify query";
            this.tsbModifyQuery.ToolTipText = "Show or hide the query panel, where you can edit the FetchXML filter used to load approvals";
            this.tsbModifyQuery.CheckedChanged += new System.EventHandler(this.tsbModifyQuery_CheckedChanged);
            //
            // queryEditor
            //
            this.queryEditor.Dock = System.Windows.Forms.DockStyle.Top;
            this.queryEditor.Location = new System.Drawing.Point(0, 50);
            this.queryEditor.Name = "queryEditor";
            this.queryEditor.Size = new System.Drawing.Size(630, 320);
            this.queryEditor.TabIndex = 7;
            this.queryEditor.Visible = false;
            //
            // splitterQuery
            //
            this.splitterQuery.Dock = System.Windows.Forms.DockStyle.Top;
            this.splitterQuery.Location = new System.Drawing.Point(0, 370);
            this.splitterQuery.MinExtra = 120;
            this.splitterQuery.MinSize = 160;
            this.splitterQuery.Name = "splitterQuery";
            this.splitterQuery.Size = new System.Drawing.Size(630, 5);
            this.splitterQuery.TabStop = false;
            this.splitterQuery.Visible = false;
            //
            // toolStripSearch
            //
            this.toolStripSearch.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.toolStripSearch.ImageScalingSize = new System.Drawing.Size(16, 16);
            this.toolStripSearch.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tslSearchIcon,
            this.tstSearch});
            this.toolStripSearch.Location = new System.Drawing.Point(0, 25);
            this.toolStripSearch.Name = "toolStripSearch";
            this.toolStripSearch.Size = new System.Drawing.Size(630, 25);
            this.toolStripSearch.TabIndex = 6;
            this.toolStripSearch.Text = "toolStrip2";
            this.toolStripSearch.Resize += new System.EventHandler(this.toolStripSearch_Resize);
            //
            // tslSearchIcon
            //
            this.tslSearchIcon.Name = "tslSearchIcon";
            this.tslSearchIcon.Size = new System.Drawing.Size(45, 22);
            this.tslSearchIcon.Text = "Search:";
            this.tslSearchIcon.ToolTipText = "Search all columns";
            this.tslSearchIcon.Click += new System.EventHandler(this.tslSearchIcon_Click);
            //
            // tstSearch
            //
            this.tstSearch.AutoSize = false;
            this.tstSearch.Name = "tstSearch";
            this.tstSearch.Size = new System.Drawing.Size(300, 23);
            this.tstSearch.ToolTipText = "Show only approvals that contain this text in any column";
            this.tstSearch.KeyDown += new System.Windows.Forms.KeyEventHandler(this.tstSearch_KeyDown);
            this.tstSearch.TextChanged += new System.EventHandler(this.tstSearch_TextChanged);
            //
            // searchDelay
            //
            this.searchDelay.Interval = 250;
            this.searchDelay.Tick += new System.EventHandler(this.searchDelay_Tick);
            //
            // tsbHeaderNames
            //
            this.tsbHeaderNames.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsbHeaderNames.Name = "tsbHeaderNames";
            this.tsbHeaderNames.Size = new System.Drawing.Size(113, 22);
            this.tsbHeaderNames.Text = "Show logical names";
            this.tsbHeaderNames.ToolTipText = "Switch the column headers between friendly (display) names and logical names";
            this.tsbHeaderNames.Click += new System.EventHandler(this.tsbHeaderNames_Click);
            //
            // tssSeparator1
            //
            this.tssSeparator1.Name = "tssSeparator1";
            this.tssSeparator1.Size = new System.Drawing.Size(6, 25);
            //
            // buttonResetSettings
            //
            this.buttonResetSettings.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.buttonResetSettings.Name = "buttonResetSettings";
            this.buttonResetSettings.Size = new System.Drawing.Size(83, 22);
            this.buttonResetSettings.Text = "Reset settings";
            this.buttonResetSettings.ToolTipText = "Reset settings";
            this.buttonResetSettings.Click += new System.EventHandler(this.buttonResetSettings_Click);
            //
            // tsbRefresh
            //
            this.tsbRefresh.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.tsbRefresh.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsbRefresh.Name = "tsbRefresh";
            this.tsbRefresh.Size = new System.Drawing.Size(50, 22);
            this.tsbRefresh.Text = "Refresh";
            this.tsbRefresh.ToolTipText = "Refresh data";
            this.tsbRefresh.Click += new System.EventHandler(this.tsbRefresh_Click);
            //
            // splitContainer1
            //
            this.splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer1.Location = new System.Drawing.Point(0, 25);
            this.splitContainer1.Margin = new System.Windows.Forms.Padding(2);
            this.splitContainer1.Name = "splitContainer1";
            //
            // splitContainer1.Panel1
            //
            this.splitContainer1.Panel1.Controls.Add(this.dataGridControl1);
            //
            // splitContainer1.Panel2
            //
            this.splitContainer1.Panel2.Controls.Add(this.webContentControl1);
            this.splitContainer1.Size = new System.Drawing.Size(630, 500);
            this.splitContainer1.SplitterDistance = 387;
            this.splitContainer1.SplitterIncrement = 2;
            this.splitContainer1.TabIndex = 5;
            //
            // dataGridControl1
            //
            this.dataGridControl1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.dataGridControl1.Location = new System.Drawing.Point(0, 0);
            this.dataGridControl1.Name = "dataGridControl1";
            this.dataGridControl1.Size = new System.Drawing.Size(384, 497);
            this.dataGridControl1.TabIndex = 0;
            //
            // webContentControl1
            //
            this.webContentControl1.Location = new System.Drawing.Point(3, 3);
            this.webContentControl1.Name = "webContentControl1";
            this.webContentControl1.Size = new System.Drawing.Size(170, 224);
            this.webContentControl1.TabIndex = 0;
            //
            // ApprovalAdminControl
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.splitContainer1);
            this.Controls.Add(this.splitterQuery);
            this.Controls.Add(this.queryEditor);
            this.Controls.Add(this.toolStripSearch);
            this.Controls.Add(this.toolStripMenu);
            this.Name = "ApprovalAdminControl";
            this.Size = new System.Drawing.Size(630, 525);
            this.OnCloseTool += new System.EventHandler(this.ApprovalAdminControl_OnCloseTool);
            this.Load += new System.EventHandler(this.ApprovalAdminControl_Load);
            this.toolStripSearch.ResumeLayout(false);
            this.toolStripSearch.PerformLayout();
            this.toolStripMenu.ResumeLayout(false);
            this.toolStripMenu.PerformLayout();
            this.splitContainer1.Panel1.ResumeLayout(false);
            this.splitContainer1.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).EndInit();
            this.splitContainer1.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private System.Windows.Forms.ToolStrip toolStripMenu;
        private System.Windows.Forms.ToolStrip toolStripSearch;
        private System.Windows.Forms.ToolStripSeparator tssSeparator2;
        private System.Windows.Forms.ToolStripButton tsbModifyQuery;
        private Controls.QueryEditorControl queryEditor;
        private System.Windows.Forms.Splitter splitterQuery;
        private System.Windows.Forms.ToolStripLabel tslSearchIcon;
        private System.Windows.Forms.ToolStripTextBox tstSearch;
        private System.Windows.Forms.Timer searchDelay;
        private System.Windows.Forms.ToolStripButton tsbHeaderNames;
        private System.Windows.Forms.ToolStripSeparator tssSeparator1;
        private System.Windows.Forms.ToolStripButton buttonResetSettings;
        private System.Windows.Forms.ToolStripButton tsbRefresh;
        private System.Windows.Forms.SplitContainer splitContainer1;
        private Controls.DataGridControl dataGridControl1;
        private Controls.WebContentControl webContentControl1;
    }
}
