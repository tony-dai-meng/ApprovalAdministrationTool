namespace ApprovalAdministrationTool
{
    partial class DataGridControl
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

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.ApprovalGridView = new System.Windows.Forms.DataGridView();
            ((System.ComponentModel.ISupportInitialize)(this.ApprovalGridView)).BeginInit();
            this.SuspendLayout();
            // 
            // ApprovalGridView
            // 
            this.ApprovalGridView.AllowUserToAddRows = false;
            this.ApprovalGridView.AllowUserToDeleteRows = false;
            this.ApprovalGridView.AllowUserToOrderColumns = true;
            this.ApprovalGridView.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.ApprovalGridView.ColumnHeadersBorderStyle = System.Windows.Forms.DataGridViewHeaderBorderStyle.None;
            this.ApprovalGridView.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.ApprovalGridView.Location = new System.Drawing.Point(2, 2);
            this.ApprovalGridView.Margin = new System.Windows.Forms.Padding(2);
            this.ApprovalGridView.Name = "ApprovalGridView";
            this.ApprovalGridView.ReadOnly = true;
            this.ApprovalGridView.RowHeadersWidth = 51;
            this.ApprovalGridView.RowTemplate.Height = 24;
            this.ApprovalGridView.Size = new System.Drawing.Size(546, 471);
            this.ApprovalGridView.TabIndex = 6;
            this.ApprovalGridView.TabStop = false;
            // 
            // DataGridControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.ApprovalGridView);
            this.Name = "DataGridControl";
            this.Size = new System.Drawing.Size(550, 475);
            ((System.ComponentModel.ISupportInitialize)(this.ApprovalGridView)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.DataGridView ApprovalGridView;
    }
}
