namespace Orts.ActivityRunner.Viewer3D.Dispatcher.Controls
{
    partial class DispatcherViewControl
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
            this.pnlMessaging = new System.Windows.Forms.Panel();
            this.pnlAvatar = new System.Windows.Forms.Panel();
            this.pnlDispatcherView = new System.Windows.Forms.Panel();
            this.pbDispatcherView = new System.Windows.Forms.PictureBox();
            this.pnlDispatcherView.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pbDispatcherView)).BeginInit();
            this.SuspendLayout();
            // 
            // pnlMessaging
            // 
            this.pnlMessaging.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(128)))), ((int)(((byte)(128)))));
            this.pnlMessaging.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnlMessaging.Location = new System.Drawing.Point(0, 0);
            this.pnlMessaging.Name = "pnlMessaging";
            this.pnlMessaging.Size = new System.Drawing.Size(750, 100);
            this.pnlMessaging.TabIndex = 1;
            // 
            // pnlAvatar
            // 
            this.pnlAvatar.BackColor = System.Drawing.Color.Green;
            this.pnlAvatar.Dock = System.Windows.Forms.DockStyle.Right;
            this.pnlAvatar.Location = new System.Drawing.Point(550, 100);
            this.pnlAvatar.Name = "pnlAvatar";
            this.pnlAvatar.Size = new System.Drawing.Size(200, 370);
            this.pnlAvatar.TabIndex = 2;
            // 
            // pnlDispatcherView
            // 
            this.pnlDispatcherView.Controls.Add(this.pbDispatcherView);
            this.pnlDispatcherView.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlDispatcherView.Location = new System.Drawing.Point(0, 100);
            this.pnlDispatcherView.Name = "pnlDispatcherView";
            this.pnlDispatcherView.Size = new System.Drawing.Size(550, 370);
            this.pnlDispatcherView.TabIndex = 3;
            // 
            // pbDispatcherView
            // 
            this.pbDispatcherView.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pbDispatcherView.Location = new System.Drawing.Point(0, 0);
            this.pbDispatcherView.Name = "pbDispatcherView";
            this.pbDispatcherView.Size = new System.Drawing.Size(550, 370);
            this.pbDispatcherView.TabIndex = 0;
            this.pbDispatcherView.TabStop = false;
            this.pbDispatcherView.SizeChanged += new System.EventHandler(this.PictureBoxDispatcherView_SizeChanged);
            // 
            // DispatcherViewControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.pnlDispatcherView);
            this.Controls.Add(this.pnlAvatar);
            this.Controls.Add(this.pnlMessaging);
            this.Name = "DispatcherViewControl";
            this.Size = new System.Drawing.Size(750, 470);
            this.KeyUp += new System.Windows.Forms.KeyEventHandler(this.DispatcherViewControl_KeyUp);
            this.pnlDispatcherView.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.pbDispatcherView)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion
        private System.Windows.Forms.Panel pnlMessaging;
        private System.Windows.Forms.Panel pnlAvatar;
        private System.Windows.Forms.Panel pnlDispatcherView;
        private System.Windows.Forms.PictureBox pbDispatcherView;
    }
}
