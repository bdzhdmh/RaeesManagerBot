using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace RaeesManagerBot;

class Program
{
    // ======================================================================
    // ۱. تنظیمات اصلی — توکن و آیدی گروه‌ها
    // ======================================================================
    private const string BOT_TOKEN = "8696733396:AAEg9jx9Kk3ZvO4WkmcnRayMa8rNTQiD5Dw";
    private const long ADMIN_GROUP_ID = -1004310491765;   // گروه مدیران (Supervisor)
    private const long TARGET_GROUP_ID = -1004482925670;  // گروه اصلی (باغ فردوس)

    // ======================================================================
    // ۲. لیست کارها
    // ======================================================================
    private static readonly List<string> MORNING_TASKS = new()
    {
        "یخچال کینو",
        "یخچال شو",
        "دیپ کلین قفسه قهوه",
        "دستگاه پشه گیر",
        "نظافت فریزر",
    };

    private static readonly List<string> NIGHT_TASKS = new()
    {
        "دیپ کلیین بک",
        "دیپ کلیین صندوق",
        "دیپ کلین دستگاه",
        "یخچال باریستا",
        "یخچال شو (کیک)",
        "فیلتر اسپیلت",
    };

    // ======================================================================
    // ۳. وضعیت کاربران + نگه‌داری اسم آن‌ها
    // ======================================================================
    private static readonly Dictionary<long, UserState> userStates = new();

    // نگه‌داری اسم کاربران بر اساس آیدی
    private static readonly Dictionary<long, string> userNames = new();

    private class UserState
    {
        public string Shift { get; set; } = "";
        public Dictionary<string, bool> Completed { get; set; } = new();
    }

    // ======================================================================
    // ۴. ساخت کیبوردها
    // ======================================================================

    // دکمه‌های انتخاب شیفت (صبح / شب) — برای گروه اصلی
    private static InlineKeyboardMarkup BuildShiftKeyboard()
    {
        return new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("☀️ شیفت صبح", "shift_morning"),
                InlineKeyboardButton.WithCallbackData("🌙 شیفت شب", "shift_night"),
            }
        });
    }

    // دکمه‌های لیست کارها
    private static InlineKeyboardMarkup BuildTaskKeyboard(string shift, Dictionary<string, bool> completed)
    {
        var tasks = shift == "morning" ? MORNING_TASKS : NIGHT_TASKS;
        var rows = new List<InlineKeyboardButton[]>();

        for (int i = 0; i < tasks.Count; i++)
        {
            var task = tasks[i];
            var check = completed.GetValueOrDefault(task, false) ? "✅" : "⬜";
            rows.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData($"{check} {task}", $"toggle_{shift}_{i}")
            });
        }

        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData("🔙 بازگشت", "back_to_shifts")
        });

        return new InlineKeyboardMarkup(rows);
    }

    // دکمه ورود به پنل مدیریت (فقط برای گروه مدیران)
    private static InlineKeyboardMarkup BuildAdminKeyboard()
    {
        return new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🛠️ ورود به پنل مدیریت", "admin_panel")
            }
        });
    }

    // کیبورد داخلی پنل مدیریت — گزارش و ریست و وضعیت
    private static InlineKeyboardMarkup BuildManagementKeyboard()
    {
        return new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("📊 دریافت گزارش (بدون ریست)", "admin_report")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🔄 گزارش + ریست کامل", "admin_reset")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("📈 وضعیت فعلی", "admin_status")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🔙 بازگشت", "admin_back")
            }
        });
    }

    // ======================================================================
    // ۵. تابع اصلی (Main)
    // ======================================================================
    private static async Task Main()
    {
        var bot = new TelegramBotClient(BOT_TOKEN);
        var me = await bot.GetMe();
        Console.WriteLine($"✅ ربات راه‌اندازی شد: @{me.Username}");

        StartWeeklyTimer(bot);

        using var cts = new CancellationTokenSource();
        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = Array.Empty<UpdateType>()
        };

        bot.StartReceiving(
            updateHandler: HandleUpdateAsync,
            errorHandler: HandleErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: cts.Token
        );

        Console.WriteLine("ربات در حال گوش دادن به پیام‌هاست... (برای خروج Enter بزن)");
        Console.ReadLine();
        cts.Cancel();
    }

    // ======================================================================
    // ۶. هندلر اصلی آپدیت‌ها
    // ======================================================================
    private static async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        try
        {
            if (update.Type == UpdateType.Message && update.Message?.Text != null)
            {
                await HandleMessageAsync(bot, update.Message, ct);
            }
            else if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery != null)
            {
                await HandleCallbackAsync(bot, update.CallbackQuery, ct);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ خطا در پردازش آپدیت: {ex.Message}");
        }
    }

    // ======================================================================
    // ۷. پردازش پیام‌های متنی
    // ======================================================================
    private static async Task HandleMessageAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var text = message.Text!;
        var chatId = message.Chat.Id;

        // دستور /start
        if (text.StartsWith("/start"))
        {
            // اگر توی گروه مدیران بودیم → پنل مدیریت
            if (chatId == ADMIN_GROUP_ID)
            {
                await bot.SendMessage(
                    chatId: chatId,
                    text: "🛠️ *پنل مدیریت*\nبرای ورود، روی دکمه زیر بزنید:",
                    parseMode: ParseMode.Markdown,
                    replyMarkup: BuildAdminKeyboard(),
                    cancellationToken: ct
                );
            }
            else
            {
                // گروه اصلی → منوی شیفت
                await bot.SendMessage(
                    chatId: chatId,
                    text: "سلام! لطفاً شیفت خود را انتخاب کنید:",
                    replyMarkup: BuildShiftKeyboard(),
                    cancellationToken: ct
                );
            }
            return;
        }

        // دستور /id
        if (text.StartsWith("/id"))
        {
            await bot.SendMessage(
                chatId: chatId,
                text: $"آیدی این چت: `{chatId}`\nنام: {message.Chat.Title ?? message.Chat.FirstName}",
                parseMode: ParseMode.Markdown,
                cancellationToken: ct
            );
        }
    }

    // ======================================================================
    // ۸. پردازش کلیک روی دکمه‌ها
    // ======================================================================
    private static async Task HandleCallbackAsync(ITelegramBotClient bot, CallbackQuery query, CancellationToken ct)
    {
        await bot.AnswerCallbackQuery(query.Id, cancellationToken: ct);

        var userId = query.From.Id;
        var data = query.Data ?? "";
        var message = query.Message;
        if (message == null) return;

        // ذخیره اسم کاربر برای استفاده در گزارش
        var firstName = query.From.FirstName ?? "";
        var lastName = query.From.LastName ?? "";
        var fullName = string.IsNullOrWhiteSpace(lastName)
            ? firstName
            : $"{firstName} {lastName}";
        if (!string.IsNullOrWhiteSpace(fullName))
        {
            userNames[userId] = fullName.Trim();
        }

        // ------------------------------------------------------------------
        // پنل مدیریت — ورود
        // ------------------------------------------------------------------
        if (data == "admin_panel")
        {
            if (message.Chat.Id != ADMIN_GROUP_ID) return;

            await bot.EditMessageText(
                chatId: message.Chat.Id,
                messageId: message.MessageId,
                text: "🛠️ *پنل مدیریت*\nیکی از گزینه‌های زیر را انتخاب کنید:",
                parseMode: ParseMode.Markdown,
                replyMarkup: BuildManagementKeyboard(),
                cancellationToken: ct
            );
            return;
        }

        // ------------------------------------------------------------------
        // پنل مدیریت — بازگشت
        // ------------------------------------------------------------------
        if (data == "admin_back")
        {
            if (message.Chat.Id != ADMIN_GROUP_ID) return;

            await bot.EditMessageText(
                chatId: message.Chat.Id,
                messageId: message.MessageId,
                text: "🛠️ *پنل مدیریت*\nبرای ورود، روی دکمه زیر بزنید:",
                parseMode: ParseMode.Markdown,
                replyMarkup: BuildAdminKeyboard(),
                cancellationToken: ct
            );
            return;
        }

        // ------------------------------------------------------------------
        // پنل مدیریت — دریافت گزارش بدون ریست
        // ------------------------------------------------------------------
        if (data == "admin_report")
        {
            if (message.Chat.Id != ADMIN_GROUP_ID) return;

            await SendWeeklyReportAndReset(bot, doReset: false, ct);
            await bot.SendMessage(
                chatId: message.Chat.Id,
                text: "✅ گزارش ارسال شد (وضعیت‌ها دست‌نخورده باقی ماندند).",
                cancellationToken: ct
            );
            return;
        }

        // ------------------------------------------------------------------
        // پنل مدیریت — گزارش + ریست کامل
        // ------------------------------------------------------------------
        if (data == "admin_reset")
        {
            if (message.Chat.Id != ADMIN_GROUP_ID) return;

            await SendWeeklyReportAndReset(bot, doReset: true, ct);
            await bot.SendMessage(
                chatId: message.Chat.Id,
                text: "✅ گزارش ارسال شد و همه وضعیت‌ها ریست شدند.",
                cancellationToken: ct
            );
            return;
        }

        // ------------------------------------------------------------------
        // پنل مدیریت — نمایش وضعیت فعلی
        // ------------------------------------------------------------------
        if (data == "admin_status")
        {
            if (message.Chat.Id != ADMIN_GROUP_ID) return;

            var statusText = $"📊 تعداد کاربران فعال در حافظه: *{userStates.Count}*\n";
            if (userStates.Count > 0)
            {
                statusText += "\n👥 لیست:\n";
                foreach (var kvp in userStates)
                {
                    var userName = userNames.GetValueOrDefault(kvp.Key, "ناشناس");
                    var shiftName = kvp.Value.Shift == "morning" ? "صبح" : "شب";
                    var done = kvp.Value.Completed.Count(x => x.Value);
                    var total = kvp.Value.Completed.Count;
                    statusText += $"• {userName} — شیفت {shiftName} — {done}/{total} ✅\n";
                }
            }

            await bot.SendMessage(
                chatId: message.Chat.Id,
                text: statusText,
                parseMode: ParseMode.Markdown,
                cancellationToken: ct
            );
            return;
        }

        // ------------------------------------------------------------------
        // حالت ۱: بازگشت به منوی شیفت‌ها
        // ------------------------------------------------------------------
        if (data == "back_to_shifts")
        {
            userStates.Remove(userId);
            await bot.EditMessageText(
                chatId: message.Chat.Id,
                messageId: message.MessageId,
                text: "لطفاً شیفت خود را انتخاب کنید:",
                replyMarkup: BuildShiftKeyboard(),
                cancellationToken: ct
            );
            return;
        }

        // ------------------------------------------------------------------
        // حالت ۲: انتخاب شیفت
        // ------------------------------------------------------------------
        if (data == "shift_morning" || data == "shift_night")
        {
            var shift = data == "shift_morning" ? "morning" : "night";

            if (!userStates.ContainsKey(userId) || userStates[userId].Shift != shift)
            {
                var tasks = shift == "morning" ? MORNING_TASKS : NIGHT_TASKS;
                userStates[userId] = new UserState
                {
                    Shift = shift,
                    Completed = tasks.ToDictionary(t => t, _ => false)
                };
            }

            var state = userStates[userId];
            var shiftName = shift == "morning" ? "صبح" : "شب";

            await bot.EditMessageText(
                chatId: message.Chat.Id,
                messageId: message.MessageId,
                text: $"📋 لیست کارهای شیفت *{shiftName}*:\nروی هر کار بزنید تا وضعیت آن تغییر کند.",
                parseMode: ParseMode.Markdown,
                replyMarkup: BuildTaskKeyboard(shift, state.Completed),
                cancellationToken: ct
            );
            return;
        }

        // ------------------------------------------------------------------
        // حالت ۳: کلیک روی یک کار (toggle)
        // ------------------------------------------------------------------
        if (data.StartsWith("toggle_"))
        {
            var parts = data.Split('_');
            var shift = parts[1];
            var taskIndex = int.Parse(parts[2]);

            if (!userStates.ContainsKey(userId))
            {
                await bot.EditMessageText(
                    chatId: message.Chat.Id,
                    messageId: message.MessageId,
                    text: "لطفاً ابتدا یک شیفت انتخاب کنید.",
                    cancellationToken: ct
                );
                return;
            }

            var state = userStates[userId];
            var tasks = shift == "morning" ? MORNING_TASKS : NIGHT_TASKS;
            var taskName = tasks[taskIndex];

            state.Completed[taskName] = !state.Completed.GetValueOrDefault(taskName, false);

            await bot.EditMessageReplyMarkup(
                chatId: message.Chat.Id,
                messageId: message.MessageId,
                replyMarkup: BuildTaskKeyboard(shift, state.Completed),
                cancellationToken: ct
            );

            // ارسال گزارش به گروه مدیران
            var statusText = state.Completed[taskName] ? "✅ انجام شد" : "❌ تیک برداشته شد";
            var user = query.From;
            var shiftName = shift == "morning" ? "صبح" : "شب";
            var displayName = string.IsNullOrWhiteSpace(lastName)
                ? firstName
                : $"{firstName} {lastName}";
            var username = string.IsNullOrEmpty(user.Username) ? "بدون_نام_کاربری" : user.Username;

            var notification =
                $"🔔 *گزارش تغییر وضعیت*\n" +
                $"👤 کاربر: {displayName}\n" +
                $"🆔 یوزرنیم: @{username}\n" +
                $"🕐 شیفت: {shiftName}\n" +
                $"📌 کار: {taskName}\n" +
                $"📊 وضعیت: {statusText}";

            try
            {
                await bot.SendMessage(
                    chatId: ADMIN_GROUP_ID,
                    text: notification,
                    parseMode: ParseMode.Markdown,
                    cancellationToken: ct
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ خطا در ارسال به گروه مدیران: {ex.Message}");
            }
        }
    }

    // ======================================================================
    // ۹. هندلر خطاها
    // ======================================================================
    private static Task HandleErrorAsync(ITelegramBotClient bot, Exception ex, CancellationToken ct)
    {
        Console.WriteLine($"❌ خطای ربات: {ex.Message}");
        return Task.CompletedTask;
    }

    // ======================================================================
    // ۱۰. تایمر گزارش هفتگی
    //     جمعه ۲۴:۰۰ به وقت ایران (شنبه ۰۰:۰۰)
    // ======================================================================
    private static void StartWeeklyTimer(ITelegramBotClient bot)
    {
        var timer = new Timer(async _ =>
        {
            try
            {
                var iranTime = GetIranTime();

                if (iranTime.DayOfWeek == DayOfWeek.Saturday &&
                    iranTime.Hour == 0 &&
                    iranTime.Minute < 1)
                {
                    await SendWeeklyReportAndReset(bot, doReset: true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ خطا در تایمر هفتگی: {ex.Message}");
            }
        }, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
    }

    private static DateTime GetIranTime()
    {
        try
        {
            var iranTz = TimeZoneInfo.FindSystemTimeZoneById("Iran Standard Time");
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, iranTz);
        }
        catch
        {
            try
            {
                var iranTz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, iranTz);
            }
            catch
            {
                return DateTime.UtcNow.AddHours(3.5);
            }
        }
    }

    // ======================================================================
    // ۱۱. ارسال گزارش + ریست اختیاری
    //     فرمت جدید: هر کار با اسم افراد انجام‌دهنده
    // ======================================================================
    private static async Task SendWeeklyReportAndReset(ITelegramBotClient bot, bool doReset, CancellationToken ct = default)
    {
        Console.WriteLine($"📊 اجرای گزارش (doReset={doReset})...");

        if (userStates.Count == 0)
        {
            try
            {
                await bot.SendMessage(
                    chatId: ADMIN_GROUP_ID,
                    text: "📭 هیچ فعالیتی در این هفته ثبت نشده است.",
                    cancellationToken: ct
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ خطا: {ex.Message}");
            }
            return;
        }

        // ------------------------------------------------------------------
        // آماده‌سازی ساختار گزارش: برای هر کار، لیست اسم افراد
        // ------------------------------------------------------------------
        var morningReport = new Dictionary<string, List<string>>();
        var nightReport = new Dictionary<string, List<string>>();

        foreach (var task in MORNING_TASKS) morningReport[task] = new List<string>();
        foreach (var task in NIGHT_TASKS) nightReport[task] = new List<string>();

        // پر کردن لیست‌ها با اسم کاربران
        foreach (var kvp in userStates)
        {
            var userId = kvp.Key;
            var state = kvp.Value;
            var userName = userNames.GetValueOrDefault(userId, "ناشناس");

            var targetReport = state.Shift == "morning" ? morningReport : nightReport;

            foreach (var task in state.Completed)
            {
                if (task.Value && targetReport.ContainsKey(task.Key))
                {
                    targetReport[task.Key].Add(userName);
                }
            }
        }

        // ------------------------------------------------------------------
        // ساخت متن گزارش
        // ------------------------------------------------------------------
        var report = "📊 *گزارش هفتگی شیفت‌ها*\n";

        // شیفت صبح
        report += "\n☀️ *شیفت صبح:*\n";
        foreach (var kvp in morningReport)
        {
            var names = kvp.Value.Count == 0
                ? "بدون انجام"
                : string.Join("، ", kvp.Value);
            report += $"• *{kvp.Key}*: {names}\n";
        }

        // شیفت شب
        report += "\n🌙 *شیفت شب:*\n";
        foreach (var kvp in nightReport)
        {
            var names = kvp.Value.Count == 0
                ? "بدون انجام"
                : string.Join("، ", kvp.Value);
            report += $"• *{kvp.Key}*: {names}\n";
        }

        // ------------------------------------------------------------------
        // ارسال گزارش به گروه مدیران
        // ------------------------------------------------------------------
        try
        {
            await bot.SendMessage(
                chatId: ADMIN_GROUP_ID,
                text: report,
                parseMode: ParseMode.Markdown,
                cancellationToken: ct
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ خطا در ارسال گزارش: {ex.Message}");
        }

        // ------------------------------------------------------------------
        // ریست اختیاری
        // ------------------------------------------------------------------
        if (doReset)
        {
            userStates.Clear();
            userNames.Clear();
            Console.WriteLine("✅ همه وضعیت‌ها ریست شدند.");
        }
        else
        {
            Console.WriteLine("ℹ️ گزارش ارسال شد، ولی وضعیت‌ها ریست نشدند.");
        }
    }
}