# ארכיטקטורה — DMX Console

## מבנה הפרויקטים

- **DmxConsole.Core** — מנוע ליבה ללא תלות ב-UI או ברשת: `Universe`, `Fixtures` (Profile/Mode/Channel/PatchedFixture/Patch), ו-`Engine` (Programmer, IOutputLayer, DmxOutputEngine).
- **DmxConsole.Protocols** — שולחי DMX על גבי רשת: `ArtNetSender` (Art-Net/ArtDMX על UDP broadcast/unicast), `SacnSender` (E1.31 על UDP multicast).
- **DmxConsole.Fixtures** — ספריית פרופילי פיקסצ'רים. כרגע `GenericFixtureLibrary` עם כמה טיפוסים גנריים; בהמשך ייתכן טעינת JSON/GDTF.
- **DmxConsole.Web** — אפליקציית Blazor Server (net8.0, Interactive Server render mode גלובלי): אותם ViewModels (CommunityToolkit.Mvvm) כמו ב-WPF הקודם, תחת `Services/`, נרשם `MainViewModel` כ-Singleton ב-DI כך שכל דפדפן/טאבלט שמתחבר משתף את אותה קונסולה חיה. קומפוננטות Razor תחת `Components/ConsoleUi/` (ראו "UI: Blazor Server" למטה). **הפרויקט הישן `DmxConsole.App` (WPF) הוסר** (commits `22b8ed5`..`a2fba70` בהיסטוריית git אם צריך לעיין).
- **DmxConsole.Core.Tests** — בדיקות יחידה ל-Core (patch overlap validation, merge engine HTP/LTP, כיול Pan/Tilt).

## זרימת הנתונים

1. המשתמש מוסיף Fixture ל-`Patch` (בחירת פרופיל+מוד+יוניברס+כתובת).
2. `DmxOutputEngine` בונה `PatchChannelMap` (מיפוי מהיר כתובת→ערוץ) בכל שינוי ב-Patch.
3. thread רקע ב-`DmxOutputEngine` רץ בקצב קבוע (ברירת מחדל 40Hz) וממזג את כל ה-`IOutputLayer` הפעילים (כרגע: `Programmer` בלבד) לפי HTP/LTP per-channel.
4. כיול Pan/Tilt (`PanTiltCalibration`) מוחל אחרי המיזוג הגנרי, per-fixture.
5. `DmxOutputEngine.UniverseOutputReady` מפרסם snapshot per-universe; ה-App מעביר אותו ל-`ArtNetSender`/`SacnSender` בהתאם לצ'קבוקסים שהמשתמש הפעיל.

## Cues / Cue List (שלב 1)

- **`Cue`** — snapshot מלא של כל ערוצי הפאץ' בזמן ההקלטה + `FadeInTime`/`FadeOutTime` נפרדים (עולה/יורד).
- **`CueList`** — `IOutputLayer` בעדיפות 150 (בין ברירת המחדל ל-Programmer). `RecordCue` קורא מה-`Programmer` (עם נפילה לברירת המחדל של הפיקסצ'ר). `Go`/`Back`/`GoToCue` מתחילים fade מהפלט הנוכחי (נשמר ב-`_currentOutput`) אל היעד; לכל ערוץ נבחר `FadeInTime` או `FadeOutTime` בהתאם לכיוון. `Stop` משחרר את השכבה לגמרי.
- ב-App: `CueListViewModel` עוטף את זה, כולל `System.Threading.Timer` (הוחלף מ-`DispatcherTimer` בעת המעבר ל-Blazor) שסוקר כל 100ms כדי לעדכן progress bar/זמן נותר (כי `CueList.Changed` נורה רק בפעולות בדידות, לא ברציפות תוך כדי fade).

## Effects (שלב 2)

- **`Effect`** (מחלקת בסיס, `DmxConsole.Core.Effects`) — קבוצת פיקסצ'רים + `SpeedHz` + `Spread` (היסט פאזה בין פיקסצ'ר לפיקסצ'ר, ליצירת "גל"). `Evaluate(elapsedSeconds)` מחזיר את כל הערוצים שהאפקט נוגע בהם לרגע נתון בבת אחת (לא per-channel, כדי לא לחשב לוגיקה כבדה 512 פעמים בטיק).
- מימושים: **`ChaseEffect`** (צעד בדיד, `Width` פיקסצ'רים דלוקים בו-זמנית), **`StrobeEffect`** (גל ריבועי לפי `DutyCycle`), **`SineEffect`** (תנודה חלקה בין `Min`-`Max`), **`RainbowEffect`** (סבב על גלגל הצבעים דרך `HsvColor.ToRgb`, דורש ערוצי Red/Green/Blue מלאים).
- **`EffectsEngine`** — `IOutputLayer` בעדיפות 300 (מעל CueList=150, מתחת ל-Programmer). גם `ITickable`: מחשב את כל האפקטים הפעילים פעם אחת בכל טיק (`Tick`) ושומר תוצאה ב-cache; `TryGetChannelValue` רק קורא מה-cache.
- **`ITickable`** — ממשק חדש ב-`Engine`; `DmxOutputEngine.Tick()` קורא ל-`Tick(elapsed)` (לפי `Stopwatch` פנימי) על כל שכבה שמממשת אותו, לפני שלב המיזוג.
- App: `EffectsViewModel` (בחירת פיקסצ'רים מרובה, טופס "אפקט חדש" עם שדות שמתחלפים לפי סוג האפקט) + טאב "Effects" ב-UI (לצד טאב "Cues").

## USB-DMX (שלב 3)

- **`EnttecProSender`** (`DmxConsole.Protocols.Usb`) — עוטף Enttec DMX USB PRO: פרוטוקול framed מעל `SerialPort` (250000 baud, 8N2) - `0x7E` + label 6 ("Output Only Send DMX") + אורך + start code + נתונים + `0xE7`. הווידג'ט עצמו מטפל בכל תזמון קו ה-DMX (break/MAB), ולכן זו הדרך האמינה יותר. בניית ה-frame חשופה כ-`BuildFrame` סטטי טהור (נבדק ב-unit tests בלי לפתוח פורט אמיתי).
- **`EnttecOpenDmxSender`** — עבור וידג'טים "גולמיים" (Open DMX): מייצר את ה-break/MAB בעצמו דרך `SerialPort.BreakState`. תזמון ה-break תלוי בתזמון thread של Windows (לא מדויק לרמת מיקרו-שנייה), אך עובד בפועל עם רוב הפיקסצ'רים; ה-PRO אמין יותר לתשתיות תובעניות.
- שני ה-senders מממשים `IDmxSender` (כמו Art-Net/sACN) ומשויכים ליוניברס בודד (`UniverseId` שנבחר ב-UI) - וידג'ט USB פיזי אחד = פלט של יוניברס אחד.
- App: `MainViewModel` מנהל `_usbSender` יחיד (Enttec Pro/Open DMX, נבחר ב-toolbar), עם רשימת COM ports (`SerialPort.GetPortNames()` דרך `EnttecProSender.ListAvailablePorts()`) וכפתור רענון.
- פרויקט בדיקות חדש: `tests/DmxConsole.Protocols.Tests` (ל-`DmxConsole.Protocols`, שאין לו תלות ב-Core-only tests).

## UI: Blazor Server (מעבר מ-WPF)

- ה-Core/Protocols/Fixtures **לא השתנו בכלל** במעבר הזה - הם נבנו מההתחלה בלי תלות ב-UI בדיוק בשביל זה.
- `src/DmxConsole.Web` (Blazor Server, net8.0, `--interactivity Server --all-interactive`): `Program.cs` רושם `MainViewModel` כ-**Singleton** ב-DI - כל דפדפן/טאבלט שמתחבר חולק את אותה קונסולה חיה (בדיוק כמו כמה תחנות שליטה על קונסולת תאורה אמיתית).
- `Services/` מכיל את אותם ViewModels שהיו ב-WPF (`MainViewModel`, `CueListViewModel`, `EffectsViewModel`, `ChannelFaderViewModel`, `FixtureSelectionItem`) כמעט ללא שינוי - הם כבר plain C# classes מבוססי `ObservableObject`/`ObservableCollection`. השינוי היחיד: `DispatcherTimer` → `System.Threading.Timer` ב-`CueListViewModel`.
- `Components/ConsoleUi/`: `Toolbar`, `PatchPanel`, `FaderBank` (+ `FaderCard` per-channel כדי לא לרנדר מחדש את כל הבנק בכל גרירה), `CueListPanel`, `EffectsPanel`. דפוס קבוע בכל קומפוננטה: `[Inject]` ל-ViewModel, הרשמה ל-`PropertyChanged`/`CollectionChanged` ב-`OnInitialized`, `InvokeAsync(StateHasChanged)` בכל שינוי, ניתוק ב-`Dispose`.
- `wwwroot/app.css`: עיצוב כהה למגע - יעד-מגע מינימלי 44px לכל פקד, סליידר אנכי דרך `input[type=range]` מסובב 90° (CSS, עובד בכל הדפדפנים - לא תלוי ב-`orient=vertical` שעובד רק ב-Firefox).
- אין HTTPS redirect בכוונה - זהו כלי מקומי/רשת מקומית, ואילוץ HTTPS היה גורם לחיכוך עם תעודות self-signed בכל טאבלט שמתחבר.

## שכבת סמנטיקה: Attributes + מספור פיקסצ'רים (Step A)

העיקרון: המשתמש עובד עם Fixtures ו-Attributes ("Group Back Wash → Color → Blue"), לא עם כתובות DMX גולמיות - זה נבנה בהדרגה, מלמטה למעלה, בלי לשבור את מה שכבר קיים.

- **`AttributeClass`** (enum ב-`ChannelType.cs`): `Intensity, Position, Color, Beam, Other`. **`ChannelTypeExtensions.ToAttributeClass()`** ממפה כל `ChannelType` קיים לקבוצה שלו (למשל כל ערוצי הצבע → `Color`, Pan/Tilt (+Fine) → `Position`). זו שכבת מיפוי בלבד - לא משנה איך `FixtureChannel`/`FixtureMode` בנויים.
- **`PatchedFixture.Number`** - מספר יציב, מוקצה אוטומטית ב-`Patch.Add` (המספר הפנוי הבא) אלא אם כבר נקבע מראש ידנית (ואינו תפוס). **`Patch.FindByNumber(int)`** - חיפוש לפי מספר, ישמש את שכבת ה-Selection ב-Step הבא.
- UI: עמודת "#" חדשה בטבלת ה-Patch (`PatchPanel.razor`) - תצוגה בלבד, עדיין אין תחביר בחירה (זה Step B).
- שום שינוי ב-`Programmer`/`Cue`/`EffectsEngine`/`DmxOutputEngine` בשלב הזה - ה-Attribute mapping והמספור הם תשתית, לא עדיין בשימוש בלוגיקת המיזוג.

## הרחבה עתידית (שלבים הבאים)

כל שלב עתידי (Visualizer 3D, Music Sync) אמור להתחבר כ-`IOutputLayer`/`ITickable` נוסף ל-`DmxOutputEngine`, או כ-`IDmxSender` נוסף בפרויקט Protocols - בלי לשנות את הליבה הקיימת. Music Sync בפרט צפוי להזין `SpeedHz`/`Spread` של אפקטים קיימים לפי BPM שזוהה, ולא לדרוש סוג שכבה חדש.
