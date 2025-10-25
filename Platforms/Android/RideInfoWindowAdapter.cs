#if ANDROID
using Android.Content;
using Android.Gms.Maps;
using Android.Gms.Maps.Model;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Text;
using Android.Views;
using Android.Widget;
using Microsoft.Maui.Controls.Platform;
using Color = Android.Graphics.Color;
using View = Android.Views.View;

namespace OrbanaDrive.Platforms.Android;

public class RideInfoWindowAdapter : Java.Lang.Object, GoogleMap.IInfoWindowAdapter
{
    private readonly Context _ctx;
    public RideInfoWindowAdapter(Context ctx) { _ctx = ctx; }

    private int Px(int dp) => (int)(_ctx.Resources!.DisplayMetrics.Density * dp);

    // ❌ NO usamos este; así evitamos el marco blanco por defecto
    public View GetInfoContents(Marker marker) => null!;

    // ✅ Devuelve la vista completa del tooltip (sin marco blanco)
    public View GetInfoWindow(Marker marker)
    {
        var titleText = (marker.Title ?? "").Trim();
        var snippetText = (marker.Snippet ?? "").Trim();
        bool isA = string.Equals(titleText, "A", System.StringComparison.OrdinalIgnoreCase);
        bool isB = string.Equals(titleText, "B", System.StringComparison.OrdinalIgnoreCase);

        var displayTitle =
        isA ? "Origen" :      // o "" si no quieres nada
        isB ? "Destino" :     // o "" si no quieres nada
          titleText;
        // Contenedor principal
        var root = new LinearLayout(_ctx) { Orientation = Orientation.Vertical };
        root.SetPadding(Px(14), Px(10), Px(14), Px(10));
        root.SetBackgroundColor(Color.Transparent); // importante

        // Fondo redondeado + borde sutil (azul/verde/gris)
        var bg = new GradientDrawable();
        if (isA) { bg.SetColor(Color.ParseColor("#1F2A44")); bg.SetStroke(Px(1), Color.ParseColor("#2C3E5C")); }
        else if (isB) { bg.SetColor(Color.ParseColor("#163524")); bg.SetStroke(Px(1), Color.ParseColor("#2A6243")); }
        else { bg.SetColor(Color.ParseColor("#1A1D24")); bg.SetStroke(Px(1), Color.ParseColor("#2A2F38")); }
        bg.SetCornerRadius(Px(12));
        root.SetBackground(bg);

        // Fila: badge + título
        var row = new LinearLayout(_ctx) { Orientation = Orientation.Horizontal };
        row.SetPadding(0, 0, 0, Px(2));

        var badge = new TextView(_ctx)
        {
            Text = string.IsNullOrEmpty(titleText) ? "•" : titleText[..1].ToUpperInvariant(),
            Gravity = GravityFlags.Center
        };
        badge.SetTextColor(Color.White);
        badge.SetTypeface(badge.Typeface, TypefaceStyle.Bold);
        badge.TextSize = 14;
        badge.SetPadding(Px(8), Px(2), Px(8), Px(2));

        var badgeBg = new GradientDrawable();
        badgeBg.SetColor(isA ? Color.ParseColor("#2F6FED")
                             : isB ? Color.ParseColor("#2ECC71")
                                   : Color.ParseColor("#2E3138"));
        badgeBg.SetCornerRadius(Px(10));
        badge.SetBackground(badgeBg);

        var title = new TextView(_ctx) { Text = displayTitle };
        title.SetTextColor(Color.White);
        title.SetTypeface(title.Typeface, TypefaceStyle.Bold);
        title.TextSize = 15;
        title.SetPadding(Px(8), 0, 0, 0);
        title.SetSingleLine(true);
        title.Ellipsize = TextUtils.TruncateAt.End;

        row.AddView(badge);
        row.AddView(title);

        // Subtítulo (dirección + “X.X km · Y min”)
        if (!string.IsNullOrWhiteSpace(snippetText))
        {
            var sub = new TextView(_ctx) { Text = snippetText };
            sub.SetTextColor(isA ? Color.ParseColor("#BFD4FF")
                                 : isB ? Color.ParseColor("#DDF6E7")
                                       : Color.Rgb(195, 205, 216));

            sub.TextSize = 12;
            sub.SetMaxLines(3);
            sub.Ellipsize = TextUtils.TruncateAt.End;

            root.AddView(row);
            root.AddView(sub);
        }
        else
        {
            root.AddView(row);
        }

        return root;
    }
}
#endif
