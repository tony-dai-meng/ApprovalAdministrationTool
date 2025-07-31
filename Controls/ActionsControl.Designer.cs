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
            this.RejectButton = new System.Windows.Forms.Button();
            this.ApproveButton = new System.Windows.Forms.Button();
            this.ReassignButton = new System.Windows.Forms.Button();
            this.flowLayoutActionH.SuspendLayout();
            this.SuspendLayout();
            // 
            // flowLayoutActionH
            // 
            this.flowLayoutActionH.Controls.Add(this.RejectButton);
            this.flowLayoutActionH.Controls.Add(this.ReassignButton);
            this.flowLayoutActionH.Controls.Add(this.ApproveButton);
            this.flowLayoutActionH.Dock = System.Windows.Forms.DockStyle.Fill;
            this.flowLayoutActionH.Location = new System.Drawing.Point(0, 0);
            this.flowLayoutActionH.Name = "flowLayoutActionH";
            this.flowLayoutActionH.Size = new System.Drawing.Size(356, 57);
            this.flowLayoutActionH.TabIndex = 0;
            this.flowLayoutActionH.Resize += new System.EventHandler(this.FlowLayoutActionH_Resize);
            // 
            // RejectButton
            // 
            //this.RejectButton.BackColor = System.Drawing.Color.White;
            this.RejectButton.BackColor = System.Drawing.Color.Gray;
            this.RejectButton.Enabled = false;
            this.RejectButton.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
            this.RejectButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.RejectButton.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(37)))), ((int)(((byte)(36)))), ((int)(((byte)(36)))));
            this.RejectButton.Location = new System.Drawing.Point(3, 3);
            this.RejectButton.Name = "RejectButton";
            this.RejectButton.Size = new System.Drawing.Size(101, 51);
            this.RejectButton.TabIndex = 1;
            this.RejectButton.Text = "Reject";
            this.RejectButton.UseVisualStyleBackColor = false;
            // 
            // ApproveButton
            // 
            //this.ApproveButton.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(98)))), ((int)(((byte)(100)))), ((int)(((byte)(167)))));
            this.ApproveButton.BackColor = System.Drawing.Color.Gray;
            this.ApproveButton.Enabled = false;
            this.ApproveButton.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
            this.ApproveButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.ApproveButton.ForeColor = System.Drawing.Color.White;
            this.ApproveButton.Location = new System.Drawing.Point(217, 3);
            this.ApproveButton.Name = "ApproveButton";
            this.ApproveButton.Size = new System.Drawing.Size(105, 51);
            this.ApproveButton.TabIndex = 0;
            this.ApproveButton.Text = "Approve";
            this.ApproveButton.UseVisualStyleBackColor = false;
            this.ApproveButton.Click += new System.EventHandler(this.ApproveButton_Click);
            // 
            // ReassignButton
            // 
            //this.ReassignButton.BackColor = System.Drawing.Color.White;
            this.ReassignButton.BackColor = System.Drawing.Color.Gray;
            this.ReassignButton.Enabled = false;
            this.ReassignButton.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
            this.ReassignButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.ReassignButton.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(37)))), ((int)(((byte)(36)))), ((int)(((byte)(36)))));
            this.ReassignButton.Location = new System.Drawing.Point(110, 3);
            this.ReassignButton.Name = "ReassignButton";
            this.ReassignButton.Size = new System.Drawing.Size(101, 51);
            this.ReassignButton.TabIndex = 2;
            this.ReassignButton.Text = "Reassign";
            this.ReassignButton.UseVisualStyleBackColor = false;
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
        private System.Windows.Forms.Button ApproveButton;
        private System.Windows.Forms.Button RejectButton;
        private System.Windows.Forms.Button ReassignButton;
    }
}
