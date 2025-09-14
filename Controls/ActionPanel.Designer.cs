namespace ApprovalAdministrationTool.Controls
{
    partial class ActionPanel
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
            this.ActionLayoutPanelV = new System.Windows.Forms.FlowLayoutPanel();
            this.actionsControl1 = new ApprovalAdministrationTool.Controls.ActionsControl();
            this.textBoxWithPlaceholder1 = new XrmToolBox.Controls.TextBoxWithPlaceholder();
            this.SubmitButton = new System.Windows.Forms.Button();
            this.ActionLayoutPanelV.SuspendLayout();
            this.SuspendLayout();
            // 
            // ActionLayoutPanelV
            // 
            this.ActionLayoutPanelV.Controls.Add(this.actionsControl1);
            this.ActionLayoutPanelV.Controls.Add(this.SubmitButton);
            this.ActionLayoutPanelV.Controls.Add(this.textBoxWithPlaceholder1);
            this.ActionLayoutPanelV.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ActionLayoutPanelV.FlowDirection = System.Windows.Forms.FlowDirection.BottomUp;
            this.ActionLayoutPanelV.Location = new System.Drawing.Point(0, 0);
            this.ActionLayoutPanelV.Name = "ActionLayoutPanelV";
            this.ActionLayoutPanelV.Size = new System.Drawing.Size(382, 250);
            this.ActionLayoutPanelV.TabIndex = 0;
            // 
            // actionsControl1
            // 
            this.actionsControl1.Location = new System.Drawing.Point(3, 190);
            this.actionsControl1.Name = "actionsControl1";
            this.actionsControl1.Size = new System.Drawing.Size(376, 57);
            this.actionsControl1.TabIndex = 0;
            // 
            // textBoxWithPlaceholder1
            // 
            this.textBoxWithPlaceholder1.ForeColor = System.Drawing.SystemColors.ControlText;
            this.textBoxWithPlaceholder1.Location = new System.Drawing.Point(385, 66);
            this.textBoxWithPlaceholder1.Multiline = true;
            this.textBoxWithPlaceholder1.Name = "textBoxWithPlaceholder1";
            this.textBoxWithPlaceholder1.Placeholder = null;
            this.textBoxWithPlaceholder1.Size = new System.Drawing.Size(376, 181);
            this.textBoxWithPlaceholder1.TabIndex = 1;
            this.textBoxWithPlaceholder1.Tag = true;
            // 
            // SubmitButton
            // 
            this.SubmitButton.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(98)))), ((int)(((byte)(100)))), ((int)(((byte)(167)))));
            this.SubmitButton.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
            this.SubmitButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.SubmitButton.ForeColor = System.Drawing.Color.White;
            this.SubmitButton.Text = "Submit";
            this.SubmitButton.UseVisualStyleBackColor = false;
            this.SubmitButton.Location = new System.Drawing.Point(3, 161);
            this.SubmitButton.Name = "SubmitButton";
            this.SubmitButton.Size = new System.Drawing.Size(376, 36);
            this.SubmitButton.TabIndex = 2;
            this.SubmitButton.UseVisualStyleBackColor = true;
            // 
            // ActionPanel
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.ActionLayoutPanelV);
            this.Name = "ActionPanel";
            this.Size = new System.Drawing.Size(382, 250);
            this.ActionLayoutPanelV.ResumeLayout(false);
            this.ActionLayoutPanelV.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.FlowLayoutPanel ActionLayoutPanelV;
        private ActionsControl actionsControl1;
        private XrmToolBox.Controls.TextBoxWithPlaceholder textBoxWithPlaceholder1;
        private System.Windows.Forms.Button SubmitButton;
    }
}
