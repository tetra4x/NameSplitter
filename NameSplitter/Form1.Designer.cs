namespace NameSplitter
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
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
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            this.tabControl = new TabControl();
            this.tabTemplate = new TabPage();
            this.tabSplit = new TabPage();
            this.tabMultiTemplate = new TabPage();
            SuspendLayout();
            // 
            // tabControl
            // 
            this.tabControl.Controls.Add(this.tabTemplate);
            this.tabControl.Controls.Add(this.tabSplit);
            this.tabControl.Controls.Add(this.tabMultiTemplate);
            this.tabControl.Dock = DockStyle.Fill;
            this.tabControl.Location = new Point(0, 0);
            this.tabControl.Name = "tabControl";
            this.tabControl.SelectedIndex = 0;
            this.tabControl.Size = new Size(800, 450);
            // 
            // tabTemplate
            // 
            this.tabTemplate.Location = new Point(4, 24);
            this.tabTemplate.Name = "tabTemplate";
            this.tabTemplate.Padding = new Padding(3);
            this.tabTemplate.Size = new Size(792, 422);
            this.tabTemplate.TabIndex = 0;
            this.tabTemplate.Text = "テンプレート作成";
            this.tabTemplate.UseVisualStyleBackColor = true;
            // 
            // tabSplit
            // 
            this.tabSplit.Location = new Point(4, 24);
            this.tabSplit.Name = "tabSplit";
            this.tabSplit.Padding = new Padding(3);
            this.tabSplit.Size = new Size(792, 422);
            this.tabSplit.TabIndex = 1;
            this.tabSplit.Text = "ネーム分割";
            this.tabSplit.UseVisualStyleBackColor = true;
            // 
            // tabMultiTemplate
            // 
            this.tabMultiTemplate.Location = new Point(4, 24);
            this.tabMultiTemplate.Name = "tabMultiTemplate";
            this.tabMultiTemplate.Padding = new Padding(3);
            this.tabMultiTemplate.Size = new Size(792, 422);
            this.tabMultiTemplate.TabIndex = 2;
            this.tabMultiTemplate.Text = "複数画像テンプレート化";
            this.tabMultiTemplate.UseVisualStyleBackColor = true;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(this.tabControl);
            Icon = (Icon)resources.GetObject("$this.Icon");
            Name = "Form1";
            Text = "NameSplitter";
            ResumeLayout(false);
        }

        #endregion

        private TabControl tabControl;
        private TabPage tabTemplate;
        private TabPage tabSplit;
        private TabPage tabMultiTemplate;
    }
}
