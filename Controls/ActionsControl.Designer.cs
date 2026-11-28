using System.Windows.Media;

namespace ApprovalAdministrationTool.Controls
{
    partial class ActionsControl
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
            this.flowLayoutActionH = new System.Windows.Forms.FlowLayoutPanel();
            this.RejectButton = new ApprovalAdministrationTool.Controls.FluentButton();
            this.ApproveButton = new ApprovalAdministrationTool.Controls.FluentButton();
            this.ReassignButton = new ApprovalAdministrationTool.Controls.FluentButton();
            this.CancelButton = new ApprovalAdministrationTool.Controls.FluentButton();
            this.flowLayoutActionH.SuspendLayout();
            this.SuspendLayout();
            // 
            // flowLayoutActionH
            // 
            this.flowLayoutActionH.Controls.Add(this.RejectButton);
            this.flowLayoutActionH.Controls.Add(this.ReassignButton);
            this.flowLayoutActionH.Controls.Add(this.ApproveButton);
            this.flowLayoutActionH.Controls.Add(this.CancelButton);
            this.flowLayoutActionH.Dock = System.Windows.Forms.DockStyle.Fill;
            this.flowLayoutActionH.Location = new System.Drawing.Point(0, 0);
            this.flowLayoutActionH.Name = "flowLayoutActionH";
            this.flowLayoutActionH.Size = new System.Drawing.Size(356, 57);
            this.flowLayoutActionH.TabIndex = 0;
            this.flowLayoutActionH.Resize += new System.EventHandler(this.FlowLayoutActionH_Resize);
            // 
            // RejectButton
            // 
            this.RejectButton.Enabled = false;
            this.RejectButton.Font = new System.Drawing.Font("Segoe UI Semibold", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.RejectButton.Location = new System.Drawing.Point(3, 3);
            this.RejectButton.Name = "RejectButton";
            this.RejectButton.Size = new System.Drawing.Size(101, 51);
            this.RejectButton.TabIndex = 1;
            this.RejectButton.Text = "Reject";
            // 
            // ApproveButton
            // 
            this.ApproveButton.Enabled = false;
            this.ApproveButton.Font = new System.Drawing.Font("Segoe UI Semibold", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.ApproveButton.Location = new System.Drawing.Point(217, 3);
            this.ApproveButton.Name = "ApproveButton";
            this.ApproveButton.Size = new System.Drawing.Size(105, 51);
            this.ApproveButton.TabIndex = 0;
            this.ApproveButton.Text = "Approve";
            // 
            // ReassignButton
            // 
            this.ReassignButton.Enabled = false;
            this.ReassignButton.Font = new System.Drawing.Font("Segoe UI Semibold", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.ReassignButton.Location = new System.Drawing.Point(110, 3);
            this.ReassignButton.Name = "ReassignButton";
            this.ReassignButton.Size = new System.Drawing.Size(101, 51);
            this.ReassignButton.TabIndex = 2;
            this.ReassignButton.Text = "Reassign";
            //
            // CancelButton
            //
            this.CancelButton.Enabled = false;
            this.CancelButton.Font = new System.Drawing.Font("Segoe UI Semibold", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.CancelButton.Location = new System.Drawing.Point(328, 3);
            this.CancelButton.Name = "CancelButton";
            this.CancelButton.Size = new System.Drawing.Size(101, 51);
            this.CancelButton.TabIndex = 3;
            this.CancelButton.Text = "Cancel";
            //
            // ActionsControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.flowLayoutActionH);
            this.Name = "ActionsControl";
            this.Size = new System.Drawing.Size(356, 57);
            this.flowLayoutActionH.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.FlowLayoutPanel flowLayoutActionH;
        private ApprovalAdministrationTool.Controls.FluentButton ApproveButton;
        private ApprovalAdministrationTool.Controls.FluentButton RejectButton;
        private ApprovalAdministrationTool.Controls.FluentButton ReassignButton;
        private ApprovalAdministrationTool.Controls.FluentButton CancelButton;
    }
}
