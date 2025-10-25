#if ANDROID
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using AndroidX.Core.Content;
using Android.Gms.Maps.Model;

namespace OrbanaDrive.Platforms.Android;

public static class MapPinIconHelper
{
    public static BitmapDescriptor FromBundle(Context ctx, string name)
    {
        int id = ctx.Resources!.GetIdentifier(name.Replace(".png", ""), "drawable", ctx.PackageName);
        if (id == 0)
            id = ctx.Resources!.GetIdentifier(name.Replace(".png", ""), "mipmap", ctx.PackageName);
        if (id == 0)
            throw new Exception($"Drawable '{name}' no encontrado.");

        var drawable = ContextCompat.GetDrawable(ctx, id)!;
        if (drawable is null) throw new Exception("Drawable nulo.");

        Bitmap bitmap;
        if (drawable is BitmapDrawable bd && bd.Bitmap is not null)
        {
            bitmap = bd.Bitmap!;
        }
        else
        {
            bitmap = Bitmap.CreateBitmap(
                drawable.IntrinsicWidth > 0 ? drawable.IntrinsicWidth : 96,
                drawable.IntrinsicHeight > 0 ? drawable.IntrinsicHeight : 96,
                Bitmap.Config.Argb8888);
            using var canvas = new Canvas(bitmap);
            drawable.SetBounds(0, 0, canvas.Width, canvas.Height);
            drawable.Draw(canvas);
        }

        return BitmapDescriptorFactory.FromBitmap(bitmap);
    }
}
#endif
