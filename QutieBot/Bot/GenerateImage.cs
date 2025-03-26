using System.Net;
using System.Text.RegularExpressions;
using QutieDAL.DAL;
using QutieDTO;
using SkiaSharp;

namespace QutieBot.Bot
{
    public class GenerateImage
    {
        private readonly GenerateImageDAL _dal;

        // Website style colors
        private readonly SKColor _backgroundColor = SKColor.Parse("#150033"); // Dark purple background
        private readonly SKColor _primaryMedium = SKColor.Parse("#520f73");  // Medium purple for accents
        private readonly SKColor _primaryLight = SKColor.Parse("#9645c4");   // Light purple for highlights
        private readonly SKColor _secondaryBlue = SKColor.Parse("#63c1ff");  // Bright blue from logo
        private readonly SKColor _secondaryPink = SKColor.Parse("#eb2f8a");  // Vibrant pink from buttons
        private readonly SKColor _backbarColor = SKColor.Parse("#2B0B4A");   // Darker purple for bars

        // Gradients will be created as needed

        // Fonts - using the same font family but with different weights/styles
        private readonly SKTypeface _fontRegular;
        private readonly SKTypeface _fontBold;
        private readonly SKTypeface _fontItalic;

        public GenerateImage(GenerateImageDAL dal)
        {
            _dal = dal;

            // Initialize fonts - trying to use more modern/sleek fonts
            // Fallback to system default if specific font not available
            _fontRegular = SKTypeface.FromFamilyName("Poppins", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                           ?? SKTypeface.FromFamilyName("Roboto", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);

            _fontBold = SKTypeface.FromFamilyName("Poppins", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                        ?? SKTypeface.FromFamilyName("Roboto", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);

            _fontItalic = SKTypeface.FromFamilyName("Poppins", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Italic)
                          ?? SKTypeface.FromFamilyName("Roboto", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Italic);
        }

        public async Task<byte[]> GenerateUserImage(long userId)
        {
            // Special case for Qutie
            if (userId == 1158671215146315796)
            {
                return await GenerateQutie(userId);
            }

            ImageDisplay userConfig = await _dal.GetImageInfoByUserId(userId);
            SKBitmap profilePicture = LoadBitmapFromUrl(userConfig.Avatar);
            SKImageInfo imageInfo = new SKImageInfo(850, 200);

            using (var surface = SKSurface.Create(imageInfo))
            {
                var canvas = surface.Canvas;

                // Create background with gradient
                using (var backgroundPaint = new SKPaint())
                {
                    using (var gradient = SKShader.CreateLinearGradient(
                        new SKPoint(0, 0),
                        new SKPoint(850, 200),
                        new SKColor[] { _backgroundColor, SKColor.Parse("#250855") },
                        new float[] { 0, 1 },
                        SKShaderTileMode.Clamp))
                    {
                        backgroundPaint.Shader = gradient;
                        canvas.DrawRect(new SKRect(0, 0, 850, 200), backgroundPaint);
                    }
                }

                // Add subtle pattern or texture (optional)
                // DrawBackgroundPattern(canvas, 850, 200);

                // Create paints for text and elements
                var headingPaint = new SKPaint
                {
                    Color = SKColors.White,
                    TextSize = 48,
                    Typeface = _fontBold,
                    IsAntialias = true
                };

                var textPaint = new SKPaint
                {
                    Color = SKColors.White,
                    TextSize = 24,
                    Typeface = _fontRegular,
                    IsAntialias = true
                };

                // Create a special paint for the name with gradient text
                var namePaint = new SKPaint
                {
                    TextSize = 48,
                    Typeface = _fontBold,
                    IsAntialias = true
                };

                // Create gradient for name text
                using (var nameGradient = SKShader.CreateLinearGradient(
                    new SKPoint(193, 53 - 48),
                    new SKPoint(193 + 300, 53),
                    new SKColor[] { _secondaryBlue, _secondaryPink },
                    null,
                    SKShaderTileMode.Clamp))
                {
                    namePaint.Shader = nameGradient;
                }

                // Karma paint with conditional color
                var karmaPaint = new SKPaint
                {
                    TextSize = 24,
                    Typeface = _fontBold,
                    IsAntialias = true
                };

                if (userConfig.Karma < 1)
                {
                    karmaPaint.Color = SKColor.Parse("#ff3860"); // Red for negative karma
                }
                else if (userConfig.Karma > 1)
                {
                    karmaPaint.Color = SKColor.Parse("#2ed573"); // Green for positive karma
                }
                else
                {
                    karmaPaint.Color = SKColors.White;
                }

                // Calculate text positions
                float textWidth = karmaPaint.MeasureText($"Karma: {userConfig.Karma:0.00}");
                float textWidth2 = textPaint.MeasureText($"Rank: {userConfig.VoiceRank}");
                float textWidth3 = textPaint.MeasureText($"Rank: {userConfig.MessageRank}");
                float textX = 823 - textWidth;
                float textX2 = 823 - textWidth2;
                float textX3 = 823 - textWidth3;

                // Draw user name with gradient
                var displayName = IsValidDisplayName(userConfig.Name) ? userConfig.Name : userConfig.FallBackName;
                canvas.DrawText(displayName, 193, 53, namePaint);

                // Draw karma
                canvas.DrawText($"Karma: {userConfig.Karma:0.00}", textX, 50, karmaPaint);

                // Draw Voice stats
                canvas.DrawText($"Voice Lvl: {userConfig.VoiceLevel}", 195, 100, textPaint);
                canvas.DrawText($"XP: {userConfig.VoiceXP}/{userConfig.VoiceReqXP}", 463, 100, textPaint);
                canvas.DrawText($"Rank: {userConfig.VoiceRank}", textX2, 100, textPaint);

                // Draw Voice XP bar - background
                int totalBarWidth = 630;
                DrawRoundedBar(canvas, 193, 113, totalBarWidth, 13, _backbarColor);

                // Draw Voice XP bar - progress with gradient
                int progressBarWidth = (int)(userConfig.VoiceXP * totalBarWidth / (float)userConfig.VoiceReqXP);
                DrawProgressBar(canvas, 193, 113, progressBarWidth, 13, _secondaryBlue, _secondaryPink);

                // Draw Message stats
                canvas.DrawText($"Message Lvl: {userConfig.MessageLevel}", 195, 160, textPaint);
                canvas.DrawText($"XP: {userConfig.MessageXP}/{userConfig.MessageReqXP}", 463, 160, textPaint);
                canvas.DrawText($"Rank: {userConfig.MessageRank}", textX3, 160, textPaint);

                // Draw Message XP bar - background
                DrawRoundedBar(canvas, 193, 175, totalBarWidth, 13, _backbarColor);

                // Draw Message XP bar - progress with gradient
                int progressBarWidth2 = (int)(userConfig.MessageXP * totalBarWidth / (float)userConfig.MessageReqXP);
                DrawProgressBar(canvas, 193, 175, progressBarWidth2, 13, _secondaryBlue, _primaryLight);

                // Draw circular avatar with border
                float centerX = 94;
                float centerY = 100;
                float radius = 85;

                // Draw avatar border (slightly larger circle behind avatar)
                using (var borderPaint = new SKPaint())
                {
                    using (var borderGradient = SKShader.CreateLinearGradient(
                        new SKPoint(centerX - radius - 4, centerY - radius - 4),
                        new SKPoint(centerX + radius + 4, centerY + radius + 4),
                        new SKColor[] { _secondaryBlue, _secondaryPink },
                        null,
                        SKShaderTileMode.Clamp))
                    {
                        borderPaint.Shader = borderGradient;
                        canvas.DrawCircle(centerX, centerY, radius + 4, borderPaint);
                    }
                }

                // Create clip path for avatar
                using (var circlePath = new SKPath())
                {
                    circlePath.AddCircle(centerX, centerY, radius);
                    canvas.Save();
                    canvas.ClipPath(circlePath);

                    // Draw avatar
                    float avatarX = centerX - radius;
                    float avatarY = centerY - radius;
                    canvas.DrawBitmap(profilePicture, new SKRect(avatarX, avatarY, avatarX + 2 * radius, avatarY + 2 * radius));

                    canvas.Restore();
                }

                // Add a subtle outer glow to the image (optional)
                // DrawOuterGlow(canvas, 850, 200);

                using (var image = surface.Snapshot())
                using (var outputStream = new MemoryStream())
                {
                    image.Encode(SKEncodedImageFormat.Png, 100).SaveTo(outputStream);
                    return outputStream.ToArray();
                }
            }
        }

        public async Task<byte[]> GenerateQutie(long userId)
        {
            ImageDisplay userConfig = await _dal.GetImageInfoByUserId(userId);
            SKBitmap profilePicture = LoadBitmapFromUrl(userConfig.Avatar);
            SKImageInfo imageInfo = new SKImageInfo(850, 200);

            if(userConfig.VoiceXP > userConfig.VoiceReqXP)
            {
                userConfig.VoiceReqXP = userConfig.VoiceXP;
            }
            if(userConfig.MessageXP > userConfig.MessageReqXP)
            {
                userConfig.MessageReqXP = userConfig.MessageXP;
            }

            using (var surface = SKSurface.Create(imageInfo))
            {
                var canvas = surface.Canvas;

                // Create background with gradient
                using (var backgroundPaint = new SKPaint())
                {
                    using (var gradient = SKShader.CreateLinearGradient(
                        new SKPoint(0, 0),
                        new SKPoint(850, 200),
                        new SKColor[] { _backgroundColor, SKColor.Parse("#250855") },
                        new float[] { 0, 1 },
                        SKShaderTileMode.Clamp))
                    {
                        backgroundPaint.Shader = gradient;
                        canvas.DrawRect(new SKRect(0, 0, 850, 200), backgroundPaint);
                    }
                }

                // Create text paints
                var textPaint = new SKPaint
                {
                    Color = SKColors.White,
                    TextSize = 24,
                    Typeface = _fontRegular,
                    IsAntialias = true
                };

                // Create special paint for the name with gradient
                var namePaint = new SKPaint
                {
                    TextSize = 48,
                    Typeface = _fontBold,
                    IsAntialias = true
                };

                using (var nameGradient = SKShader.CreateLinearGradient(
                    new SKPoint(193, 53 - 48),
                    new SKPoint(193 + 300, 53),
                    new SKColor[] { _secondaryBlue, _secondaryPink },
                    null,
                    SKShaderTileMode.Clamp))
                {
                    namePaint.Shader = nameGradient;
                }

                // Draw Qutie name
                canvas.DrawText(userConfig.Name, 193, 53, namePaint);

                // Draw XP stats
                canvas.DrawText($"Total Banked Voice XP:", 195, 100, textPaint);
                canvas.DrawText($"{userConfig.VoiceXP}/{userConfig.VoiceReqXP}", 590, 100, textPaint);

                // Draw Voice XP bar
                int totalBarWidth = 580;
                DrawRoundedBar(canvas, 193, 113, totalBarWidth, 13, _backbarColor);

                int progressBarWidth = (int)(userConfig.VoiceXP * totalBarWidth / (float)userConfig.VoiceReqXP);
                DrawProgressBar(canvas, 193, 113, progressBarWidth, 13, _secondaryBlue, _secondaryPink);

                // Draw Message XP stats
                canvas.DrawText($"Total Banked Message XP:", 195, 160, textPaint);
                canvas.DrawText($"{userConfig.MessageXP}/{userConfig.MessageReqXP}", 590, 160, textPaint);

                // Draw Message XP bar
                DrawRoundedBar(canvas, 193, 175, totalBarWidth, 13, _backbarColor);

                int progressBarWidth2 = (int)(userConfig.MessageXP * totalBarWidth / (float)userConfig.MessageReqXP);
                DrawProgressBar(canvas, 193, 175, progressBarWidth2, 13, _secondaryBlue, _primaryLight);

                // Draw avatar with border
                float centerX = 94;
                float centerY = 100;
                float radius = 85;

                // Draw avatar border
                using (var borderPaint = new SKPaint())
                {
                    using (var borderGradient = SKShader.CreateLinearGradient(
                        new SKPoint(centerX - radius - 4, centerY - radius - 4),
                        new SKPoint(centerX + radius + 4, centerY + radius + 4),
                        new SKColor[] { _secondaryBlue, _secondaryPink },
                        null,
                        SKShaderTileMode.Clamp))
                    {
                        borderPaint.Shader = borderGradient;
                        canvas.DrawCircle(centerX, centerY, radius + 4, borderPaint);
                    }
                }

                // Draw avatar
                using (var circlePath = new SKPath())
                {
                    circlePath.AddCircle(centerX, centerY, radius);
                    canvas.Save();
                    canvas.ClipPath(circlePath);

                    float avatarX = centerX - radius;
                    float avatarY = centerY - radius;
                    canvas.DrawBitmap(profilePicture, new SKRect(avatarX, avatarY, avatarX + 2 * radius, avatarY + 2 * radius));

                    canvas.Restore();
                }

                using (var image = surface.Snapshot())
                using (var outputStream = new MemoryStream())
                {
                    image.Encode(SKEncodedImageFormat.Png, 100).SaveTo(outputStream);
                    return outputStream.ToArray();
                }
            }
        }

        public async Task<byte[]> GenerateVoiceRank()
        {
            List<ImageDisplay> userConfig = await _dal.GetTopVoiceLevelUsers();
            return await GenerateRankImage(userConfig, true);
        }

        public async Task<byte[]> GenerateMessageRank()
        {
            List<ImageDisplay> userConfig = await _dal.GetTopMessageLevelUsers();
            return await GenerateRankImage(userConfig, false);
        }

        public async Task<byte[]> GenerateLeaderboard()
        {
            List<ImageDisplay> userConfig = await _dal.GetTopLevelUsers();
            return await GenerateRankImage(userConfig, false);
        }

        private async Task<byte[]> GenerateRankImage(List<ImageDisplay> userConfig, bool isVoiceRank)
        {
            SKImageInfo imageInfo = new SKImageInfo(680, 702);

            using (var surface = SKSurface.Create(imageInfo))
            {
                var canvas = surface.Canvas;

                // Create background with gradient
                using (var backgroundPaint = new SKPaint())
                {
                    using (var gradient = SKShader.CreateLinearGradient(
                        new SKPoint(0, 0),
                        new SKPoint(680, 702),
                        new SKColor[] { _backgroundColor, SKColor.Parse("#250855") },
                        new float[] { 0, 1 },
                        SKShaderTileMode.Clamp))
                    {
                        backgroundPaint.Shader = gradient;
                        canvas.DrawRect(new SKRect(0, 0, 680, 702), backgroundPaint);
                    }
                }

                // Create header text paint
                var headertextPaint = new SKPaint
                {
                    Color = SKColors.White,
                    TextSize = 30,
                    Typeface = _fontBold,
                    IsAntialias = true
                };

                // Draw decorative border with gradient
                DrawGradientBorder(canvas, 0, 0, 680, 702, 2, _secondaryBlue, _secondaryPink);

                // Process each user
                int yPosition = 48;
                int yPositionA = 2;
                int yPositionB = 70;
                int yPositionborder = 70;
                int yPositionborder2 = 72;

                foreach (var userInfo in userConfig)
                {
                    // Load and draw user avatar
                    SKBitmap avatar = LoadBitmapFromUrl(userInfo.Avatar);
                    canvas.DrawBitmap(avatar, new SKRect(2, yPositionA, 72, yPositionB));

                    // Draw user info
                    var name = IsValidDisplayName(userInfo.Name) ? userInfo.Name : userInfo.FallBackName;
                    int rank = isVoiceRank ? userInfo.VoiceRank : userInfo.MessageRank;
                    int level = isVoiceRank ? userInfo.VoiceLevel : userInfo.MessageLevel;

                    canvas.DrawText($"#{rank} - {name} - LVL: {level}", 86, yPosition, headertextPaint);

                    // Draw separator line with gradient
                    DrawGradientLine(canvas, 0, yPositionborder, 680, yPositionborder2, _primaryMedium, _primaryLight);

                    // Update positions for next entry
                    yPosition += 70;
                    yPositionborder += 70;
                    yPositionborder2 += 70;
                    yPositionA += 70;
                    yPositionB += 70;
                }

                using (var image = surface.Snapshot())
                using (var outputStream = new MemoryStream())
                {
                    image.Encode(SKEncodedImageFormat.Png, 100).SaveTo(outputStream);
                    return outputStream.ToArray();
                }
            }
        }

        #region Helper Methods

        private void DrawRoundedBar(SKCanvas canvas, float x, float y, float width, float height, SKColor color)
        {
            using (var paint = new SKPaint { Color = color })
            {
                float radius = height / 2;
                canvas.DrawRoundRect(new SKRoundRect(new SKRect(x, y, x + width, y + height), radius, radius), paint);
            }
        }

        private void DrawProgressBar(SKCanvas canvas, float x, float y, float width, float height, SKColor startColor, SKColor endColor)
        {
            if (width <= 0) return;

            using (var paint = new SKPaint())
            {
                using (var gradient = SKShader.CreateLinearGradient(
                    new SKPoint(x, y),
                    new SKPoint(x + width, y),
                    new SKColor[] { startColor, endColor },
                    null,
                    SKShaderTileMode.Clamp))
                {
                    paint.Shader = gradient;

                    float radius = height / 2;
                    canvas.DrawRoundRect(new SKRoundRect(new SKRect(x, y, x + width, y + height), radius, radius), paint);
                }
            }
        }

        private void DrawGradientBorder(SKCanvas canvas, float x, float y, float width, float height, float thickness, SKColor startColor, SKColor endColor)
        {
            // Top border
            DrawGradientLine(canvas, x, y, x + width, y + thickness, startColor, endColor);

            // Left border
            DrawGradientLine(canvas, x, y, x + thickness, y + height, startColor, endColor);

            // Right border
            DrawGradientLine(canvas, x + width - thickness, y, x + width, y + height, startColor, endColor);

            // Bottom border
            DrawGradientLine(canvas, x, y + height - thickness, x + width, y + height, startColor, endColor);
        }

        private void DrawGradientLine(SKCanvas canvas, float x1, float y1, float x2, float y2, SKColor startColor, SKColor endColor)
        {
            using (var paint = new SKPaint())
            {
                using (var gradient = SKShader.CreateLinearGradient(
                    new SKPoint(x1, y1),
                    new SKPoint(x2, y2),
                    new SKColor[] { startColor, endColor },
                    null,
                    SKShaderTileMode.Clamp))
                {
                    paint.Shader = gradient;
                    canvas.DrawRect(new SKRect(x1, y1, x2, y2), paint);
                }
            }
        }

        private bool IsValidDisplayName(string name)
        {
            // Allow letters, numbers, spaces, and basic punctuation
            string pattern = @"^[a-zA-Z0-9\s\p{P}\p{Sm}\p{Sc}\p{Sk}\p{So}\p{L}]+$";
            return Regex.IsMatch(name, pattern);
        }

        private static SKBitmap LoadBitmapFromUrl(string url)
        {
            using (WebClient webClient = new WebClient())
            {
                try
                {
                    byte[] data = webClient.DownloadData(url);
                    using (MemoryStream stream = new MemoryStream(data))
                    {
                        return SKBitmap.Decode(stream);
                    }
                }
                catch (Exception)
                {
                    // Return a placeholder image or default avatar on error
                    return CreatePlaceholderAvatar();
                }
            }
        }

        private static SKBitmap CreatePlaceholderAvatar()
        {
            // Create a simple placeholder avatar with initials or icon
            SKBitmap placeholder = new SKBitmap(200, 200);

            using (SKCanvas canvas = new SKCanvas(placeholder))
            {
                // Draw background
                canvas.Clear(SKColor.Parse("#520f73"));

                // Draw user icon or text
                using (var paint = new SKPaint
                {
                    Color = SKColors.White,
                    TextSize = 80,
                    TextAlign = SKTextAlign.Center,
                    IsAntialias = true
                })
                {
                    canvas.DrawText("?", 100, 130, paint);
                }
            }

            return placeholder;
        }

        #endregion
    }
}