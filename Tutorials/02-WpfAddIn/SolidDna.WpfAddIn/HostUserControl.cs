using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using AngelSix.SolidDna;

namespace SolidDna.WpfAddIn
{
    [ProgId(MyProgId)]
    public partial class HostUserControl : UserControl, ITaskpaneControl
    {
        private const string MyProgId = "SolidDna.Test.HostUI";
        public HostUserControl()
        {
            InitializeComponent();
        }

        public string ProgId => MyProgId;
    }
}
