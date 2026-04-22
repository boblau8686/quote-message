// ── Ensure System.IO is always available ──────────────────────────────────
// (WPF+WinForms hybrid projects don't always inherit the base SDK implicit using)
global using System.IO;

// ── Disambiguate WPF vs WinForms / System.Drawing naming conflicts ─────────
// WPF implicit usings pull in System.Windows.* and System.Windows.Media.*
// WinForms implicit usings pull in System.Windows.Forms.* and System.Drawing.*
// When both are enabled the short names below become ambiguous — pin them to
// the WPF variants, which is what this project uses everywhere.

// System.Windows.Application  vs  System.Windows.Forms.Application
global using Application = System.Windows.Application;

// System.Windows.Media.Brush  vs  System.Drawing.Brush
global using Brush       = System.Windows.Media.Brush;

// System.Windows.Media.Brushes  vs  System.Drawing.Brushes
global using Brushes     = System.Windows.Media.Brushes;

// System.Windows.Media.Color  vs  System.Drawing.Color
global using Color       = System.Windows.Media.Color;

// System.Windows.Size  vs  System.Drawing.Size
global using Size        = System.Windows.Size;

// MsgDots.Models.Message  vs  System.Windows.Forms.Message
global using Message     = MsgDots.Models.Message;
