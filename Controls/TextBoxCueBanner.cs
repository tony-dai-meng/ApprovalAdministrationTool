using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ApprovalAdministrationTool.Controls
{
    /// <summary>Grey placeholder text shown in an empty text box (the Windows "cue banner").</summary>
    internal static class TextBoxCueBanner
    {
        private const int EM_SETCUEBANNER = 0x1501;

        public static void Set(TextBox textBox, string text)
        {
            if (textBox.IsHandleCreated)
            {
                SendMessage(textBox.Handle, EM_SETCUEBANNER, (IntPtr)1, text);
            }

            textBox.HandleCreated += (sender, e) => SendMessage(textBox.Handle, EM_SETCUEBANNER, (IntPtr)1, text);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
    }
}
