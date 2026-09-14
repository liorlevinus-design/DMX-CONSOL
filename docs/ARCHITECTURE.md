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

## Selection & Groups (Step B)

- **`DmxConsole.Core.Selection`** (namespace חדש) - שכבת query בלבד מעל `Patch`, בלי שום השפעה על `Programmer`/`Cue`/`DmxOutputEngine`.
- **`FixtureSelection`** - סט **מסודר** (לא HashSet - הסדר קובע Next/Previous ו-Odd/Even): `Toggle/Add/Remove/Clear`, `SelectRange(patch, from, to)` (טווח לפי `Number`, כולל שני הקצוות, מוסיף לבחירה הקיימת - לא מחליף), `FilterOdd()`/`FilterEven()` (שומרים לפי **מיקום בסדר הבחירה** - 1st/3rd/5th... ו-2nd/4th/6th..., לא לפי parity של ה-Number עצמו), `Next(patch)`/`Previous(patch)` (מזיזים סמן פיקסצ'ר-יחיד לפי `Number` מסודר על כל ה-patch, גולש בקצוות, **מחליף** את הבחירה).
- **`FixtureGroup`** + **`GroupManager`** - קבוצה היא שם + רשימת references ל-`PatchedFixture` (לא snapshot ערכים); `CreateFromSelection` מצלם את החברים הנוכחיים של הבחירה לרשימה חדשה ובלתי-תלויה.
- App: **`SelectionViewModel`** (טופס Thru דו-שלבי - "Thru" חמוש על הפריט האחרון שנבחר, הקלקה הבאה על מספר משלימה את הטווח), **`SelectionBar.razor`** (כפתורי מספר + Thru/Odd/Even/Next/Previous/Clear + צ'יפים של קבוצות + שמירה כקבוצה) בין ה-main-grid לטאבים. `FaderCard.razor` מקבל `Selection` כפרמטר ומדגיש (`.fader.selected`) פיקסצ'רים נבחרים - עדיין מציג את כל הערוצים, לא מסנן (זה Step C).
- נבדק ידנית מקצה לקצה בדפדפן: toggle, Thru range, Odd/Even, Next, Save/Recall Group - כולם עובדים ומסונכרנים חזותית בין ה-Selection Bar ל-Fader Bank.

## Command/Service Layer — DmxConsole.Application (Step C0)

**המניע:** כל מוטציה עתידית (Programmer, Presets, Cues, Playback) צריכה לעבור דרך שכבה אחת מסודרת - כדי ש-Undo/Redo יעבדו באמת, וכדי ששכבת Natural-Language Programmer עתידית (voice/text → Commands) תוכל להיות **client** נוסף של אותה שכבה בדיוק כמו ה-UI, בלי לגעת ב-`DmxConsole.Core` ישירות. זו הסיבה שהוכנס **עכשיו**, לפני שדרוג ה-Programmer.

- פרויקט חדש **`DmxConsole.Application`** (לא `DmxConsole.Console` - שם שמתנגש מושגית עם `System.Console`). מפנה אך ורק ל-`DmxConsole.Core`.
- **`ConsoleContext`** - state תפעולי אמיתי בלבד (`Patch`, `Programmer`, `FixtureSelection`, `GroupManager`; בעתיד: CueLists, PresetLibrary). **לא** מכיל "זיכרון שיחה" (כמו "עוד 10%") - זה נשאר בלעדית בשכבת ה-NL העתידית, שתמיר משפט טבעי ל-Commands מפורשים לפני שהיא בכלל נוגעת ב-`ConsoleContext`.
- **`CommandResult`** - structured data קודם כל (`ActionType` enum, `AffectedFixtures`, `Group`, `Success`/`Error`/`Warning`); `Message` הוא convenience בלבד ל-UI/דיבוג - שכבת NL עתידית תקרא את השדות המובנים, לא תפרש את ה-`Message`.
- **`IConsoleCommand`** - `Execute(context)` + `Undo(context)`. **`SelectionCommandBase`** (ב-`Commands/Selection/`) מיישם Undo אחיד לכל פקודות הבחירה: תופס snapshot מלא של הבחירה לפני המוטציה, `Undo` משחזר אותו במלואו - נכון-מבנייה, בלי לנמק היפוך ספציפי לכל פעולה (Toggle/Range/Odd/Even/Next/Previous/AddGroup).
- **`CreateGroupCommand`**/**`RemoveGroupCommand`** (ב-`Commands/Groups/`) - `RemoveGroupCommand` שומר את ה-index המקורי ומחזיר אליו ב-Undo; נכשל בעדינות (`CommandResult.Failed`) אם הקבוצה כבר לא קיימת.
- **`CommandDispatcher.Dispatch`** - מפעיל ודוחף ל-Undo stack אם הצליח. **`DispatchBatch`** עוטף רשימת פקודות ב-**`CompositeCommand`** (`Commands/CompositeCommand.cs`) ומפעיל דרך אותו `Dispatch` - כך שכמה מוטציות ממשפט אחד ("תוריד את הווש הקר ותעלה את הפרונטים") נהפכות ב-`Undo()` **אחד**. `CompositeCommand` הוא **טרנזקציה אמיתית**: אם פקודה כלשהי נכשלת באמצע, כל מה שכבר הצליח באותו batch עובר rollback (Undo בסדר הפוך) לפני שה-`Execute` חוזר, ולכן `Dispatch` (שדוחף ל-undo stack רק אם `Success`) אף פעם לא דוחף batch שנכשל. תוצאת ה-batch (`CommandResult` עם `ActionType=Batch`) חושפת `ChildResults` - התוצאה המובנית של **כל** פקודת-בת (כולל זו שנכשלה), כדי שצרכן (כמו NL layer עתידי) ידע בדיוק מה קרה בכל שלב, לא רק בפעולה האחרונה.
- **`UndoRedoService`** - שני `Stack<IConsoleCommand>`; `Redo()` קורא שוב ל-`Execute` (לא שומר את התוצאה הישנה) כדי שהפקודה תתפוס snapshot טרי להיפוך הבא שלה; `Push` חדש מנקה את ה-redo stack.
- **`SelectionViewModel.cs`** (Web) עודכן: כל `[RelayCommand]` בונה `IConsoleCommand` ומעביר ל-`CommandDispatcher` במקום לגעת ב-`FixtureSelection`/`GroupManager` ישירות. `Selection`/`Groups` נשארו properties ציבוריים (עכשיו proxy ל-`ConsoleContext`) - **אפס שינוי** נדרש ב-`SelectionBar.razor`/`FaderCard.razor`. `ArmThru` (מצב "Thru חמוש") נשאר UI-בלבד - זו זרימת מגע רגילה, לא state קונסולאי או NL.
- `MainViewModel` בונה את ה-`ConsoleContext`/`UndoRedoService`/`CommandDispatcher`, וטולבר קיבל שני כפתורי Undo/Redo גלובליים.
- פרויקט בדיקות חדש `tests/DmxConsole.Application.Tests` - כולל בדיקת batch-transaction ייעודית שמוודאת ש-`Undo()` אחד הופך שתי פקודות שהופעלו יחד.

## שדרוג Programmer: Release/ClearAttribute/Knockout/AdjustIntensity (Step C1)

- **`Programmer.cs`** ([Programmer.cs](src/DmxConsole.Core/Engine/Programmer.cs)) - tri-state Knockout, בלי תלות ב-Patch (נשאר address-level): `Knockout`/`Restore`/`IsKnockedOut` + `HasStoredValue` (שאילתת-אדמין שמתעלמת מ-knockout, בניגוד ל-`TryGetChannelValue` שמשמש את המיזוג ומחזיר `false` כשה-knocked-out גם אם יש ערך שמור). `ClearChannel`/`ClearAll` מנקים גם knockout.
- **`PatchedFixture.ChannelsForAttribute(AttributeClass)`** - עטיפה סביב Step A.
- **`ProgrammerChannelCommandBase`** (`DmxConsole.Application.Commands.Programmer`) - אותו דפוס כמו `SelectionCommandBase`: snapshot (`HadValue`, `Value`, `WasKnockedOut`) לכל ערוץ נוגע *לפני* המוטציה, `Undo` משחזר במלואו. גם ממלא את `CommandResult.PreviousValues`/`NewValues`/`AffectedAttributes` (הרחבות חדשות ל-`CommandResult`, keyed לפי `(FixtureId, ChannelType)` ולא כתובת גולמית) - גנרי לכל 4 הפקודות.
- **`ReleaseCommand(targets, AttributeClass? attribute = null)`** - `null`→Release מלא, מוגדר→`ClearAttribute` (nullable filter הוא כל ההבדל, אין שתי מחלקות). **`KnockoutCommand`/`RestoreCommand`** - no-op (לא "משפיע") אם אין ערך שמור / כבר במצב המבוקש.
- **`AdjustIntensityCommand(targets, AdjustOperation, percent)`** - מוגבל ל-`AttributeClass.Intensity` **בקומפילציה** (מועבר קבוע ל-base, לא בדיקת runtime) - Position/Color/Beam נדחים לצעד עתידי. `Relative` = תוספת נקודות אחוז (קליפ 0-100), `Absolute` = קביעה מדויקת. **מגבלה מודעת:** "הערך הנוכחי" הוא מה שה-Programmer עצמו מחזיק (או `DefaultValue`) - לא הפלט הממוזג בפועל (יכול לנבוע מ-Cue/Effect); זה ידרוש חשיפת snapshot מ-`DmxOutputEngine` שעדיין לא קיימת.
- **באג שהתגלה ותוקן באימות הידני:** `ChannelFaderViewModel.Value` לא התעדכן כשפקודת Programmer (לא גרירת פאדר) שינתה ערך - כי שני מסלולי הכתיבה (גרירה, Command) לא היו מסונכרנים בחזרה ל-UI. **`ProgrammerViewModel.RefreshAllFaders()`** קורא מ-`Programmer.HasStoredValue` (fallback ל-`DefaultValue`) ומעדכן את כל הפאדרים - נקרא אחרי כל Dispatch מוצלח ב-`ProgrammerViewModel`, וגם אחרי Undo/Redo גלובליים ב-`MainViewModel` (כי אלה יכולים להפוך כל פקודת Programmer, לא רק כאלה שנשלחו מ-`ProgrammerViewModel`).
- UI: **`ProgrammerPanel.razor`** (Release/Clear×4/Knockout/Restore/±% Intensity) מתחת ל-`SelectionBar`. **`FaderBank.razor`** משודרג: מסנן ל-`Selection.Items` בלבד, מקבץ לפי `AttributeClass` (כותרת per קבוצה, סדר Intensity→Position→Color→Beam→Other); בחירה ריקה → הודעה במקום רשימה שקטה.
- נבדק ידנית מקצה לקצה: Raise/Lower/Undo/Redo, Knockout (פאדר עדיין מציג את הערך, `TryGetChannelValue` false), Release (פאדר חוזר ל-DefaultValue), קיבוץ Attribute עם 2 פיקסצ'רים שונים (RGBW + Moving Head) מציג ארבע קבוצות נכון.

### תיקון: Relative Adjustment קורא את הפלט הממוזג האמיתי, לא רק את ה-Programmer

לאחר סקירת המשתמש: "תרים את הפרונטים ב-20" כשיש Cue פעיל שכבר מוציא 60% (בלי ערך ב-Programmer בכלל) צריך לתת 80%, לא 20%. הפתרון (`IEffectiveOutputReader.cs`):

- **`IEffectiveOutputReader`** (`DmxConsole.Core.Engine`) - ממשק צר, read-only: `GetEffectiveValue(universeId, channelIndex)`. **`DmxOutputEngine`** מממש אותו ישירות (`_universes.TryGetValue(...) ? universe[idx] : 0`) - **בלי שינוי מבני**, כי `Universe` כבר שומר את התוצאה הממוזגת האחרונה מכל Tick; זו הייתה תוספת קטנה ונקייה, לא ארכיטקטורה חדשה.
- **`ConsoleContext.EffectiveOutput`** - שדה חדש (`IEffectiveOutputReader`, לא `DmxOutputEngine` המלא - כדי ש-Commands לעולם לא יגיעו ל-lifecycle/layers של המנוע, רק לקריאה).
- **`ProgrammerChannelCommandBase.ApplyToChannel`** - שינה חתימה מ-`(Programmer, ...)` ל-`(ConsoleContext, ...)` כדי ש-`AdjustIntensityCommand` יוכל לקרוא גם מ-`EffectiveOutput`. **`AdjustIntensityCommand`**: `Relative` קורא כעת `context.EffectiveOutput.GetEffectiveValue(...)` (לא `Programmer.HasStoredValue ?? DefaultValue`) - "הערך הנוכחי" הוא מה שבאמת יוצא (Cue/Effect/Programmer/default, איזה שהוא באמת מנצח במיזוג), לא רק שכבת ה-Programmer. `Absolute` לא השתנה (לא תלוי בערך נוכחי כלל).
- **מגבלה מתועדת, לא באג:** אם universe מעולם לא עבר Tick (המנוע לא הופעל), אין "פלט אפקטיבי" עדיין - `GetEffectiveValue` מחזיר 0. זהה למצב "אין פלט בפועל", לא edge-case שדורש טיפול.
- נבדק ידנית מקצה לקצה בתרחיש אמיתי: הקלטת Cue ב-50% → Clear Programmer → GO → "+Raise 10%" → הפאדר הציג **154** (≈60%, לא ≈26 שהיה קורה עם הבאג) - מאשר שההתאמה היחסית קוראת נכון את הפלט הממוזג האמיתי מה-Cue, לא Programmer ריק.
- Position/Color/Beam relative adjustment **עדיין לא בשלב הזה** - נותר Intensity בלבד, כפי שאושר.

## Presets/Palettes (Step D)

לפני התכנון נקרא מדריך MagicQ המלא (285 עמ') ועמוד ה-Presets של grandMA3 - שניהם אישרו ש-4 ה-`AttributeClass` שלנו (Intensity/Position/Color/Beam) הם בדיוק 4 ה-pools הנפרדים שכל קונסולה מקצועית משתמשת בהם, ושה-Cue-by-reference (Step E הבא) הוא בדיוק מה ש-grandMA3 עושה ("store a labeled reference, rather than the actual value itself").

- **`Preset`** / **`PresetLibrary`** (`DmxConsole.Core.Presets`, namespace מקביל ל-`Selection`) - `Preset.Values` הוא `Dictionary<ChannelType, byte>`, **לא** לפי כתובת גולמית - זה מה שמאפשר לאותו preset לחול על סוגי פיקסצ'רים שונים (מקביל ל-"Universal mode" הפשוט של grandMA3). מספור עצמאי **פר-`AttributeClass`** (בדיוק כמו 4 ה-pools הנפרדים ב-MagicQ) - `PresetLibrary.NextFreeNumber(cls)`.
- **`ApplyPresetCommand : ProgrammerChannelCommandBase`** - `AttributeFilter = preset.Class`, כך שכל תשתית ה-Undo/PreviousValues/NewValues מ-Step C1 מגיעה בחינם. `ApplyToChannel` כותב רק אם `preset.Values` מכיל את ה-`ChannelType` של הערוץ - פיקסצ'ר שחסר לו ערוץ מסוים בפרסט פשוט לא נוגע בו, בלי שגיאה.
- **`StorePresetCommand`** - `IConsoleCommand` ישיר (לא דרך ה-base, כי הוא כותב ל-Preset, לא ל-Programmer). קורא מה-Programmer כמו `CueList.RecordCue` הקיים (`HasStoredValue ?? DefaultValue`) - **לא** מ-`EffectiveOutput` (זה "מה שאני רוצה לשמור", לא "מה שרואים כרגע" - סמנטיקה שונה מה-Relative Adjustment). **ממזג** בהקלטה חוזרת: רק `ChannelType`-ים שבאמת קיימים בין ה-targets מתעדכנים; אחרים שכבר שמורים ב-Preset נשארים (בדיוק כמו MagicQ). נכשל בעדינות (`CommandResult.Failed`) אם אף target לא תואם ל-Class.
- **`RemovePresetCommand`** - כמו `RemoveGroupCommand` (index capture, Undo מכניס בחזרה).
- **`CommandResult` הפך ל-`record`** (מ-`class`) כדי לאפשר `with` expressions - `ProgrammerChannelCommandBase` מקבל hook וירטואלי חדש `DecorateResult(result)` כדי ש-`ApplyPresetCommand` יוכל לצרף `Preset = preset` לתוצאה בלי לשכפל את כל לוגיקת ה-Execute.
- **`ConsoleContext`** מקבל `PresetLibrary Presets` (פרמטר קונסטרוקטור נוסף).
- UI: טאב שלישי **"Presets"** (`PresetPanel.razor`) ליד Cues/Effects - בורר Class, טבלת presets (Apply/Remove), טופס Record New/Update Selected. `PresetViewModel` מקבל `ProgrammerViewModel` כדי לקרוא `RefreshAllFaders()` אחרי Apply - **אותו באג בדיוק מ-Step C1**, לא חזרנו עליו.
- **נבדק ידנית בדפדפן**: הקלטת Color preset מ-RGBW Par (Red=255,Blue=128) → Apply על Moving Head (רק Color Wheel, אין ChannelType תואם) → "Applied Color preset 1 to **0** fixture(s)" נכון; הקלטת Position preset ממאבר-הד אחד (Pan=200,Tilt=100) → Apply על מאבר-הד שני → "Applied... to **1** fixture(s)" והפאדרים מציגים בדיוק 200/100 מיד.

## Cue by reference - מודל נתונים (Step E)

לפני התכנון קראנו שוב במדריכי MagicQ ו-grandMA3, בפוקוס על שלושה נושאים: timing per-attribute, preset-reference lifecycle, ו-tracking/ownership. הממצא המרכזי (grandMA3): *"store a labeled reference, rather than the actual value itself... updating the preset means the cues do not need to be updated"* - Resolution של `PresetRef` **חייב** לקרות בזמן קריאה (playback), לא בזמן הקלטה. שינוי זה הוא **Core בלבד** - שום Command/UI חדש ל-Cue/CueList (`CueListViewModel` ממשיך לקרוא ל-`CueList` ישירות, בדיוק כמו היום).

- **`CueValue`** (`DmxConsole.Core.Engine`, חדש) - מה ש-`Cue.Levels` מאחסן לכל כתובת: `Absolute(ChannelType, byte)` או `FromPreset(ChannelType, Guid presetId)`. ה-`ChannelType` נשמר תמיד (גם ל-Absolute) - נדרש הן לפתרון PresetRef והן לקביעת ה-`AttributeClass` לצורך timing precedence, בלי ש-`CueList` יזדקק ל-`Patch` בכלל. `TryResolve(IPresetResolver?, out byte)` נפתר **מחדש בכל קריאה** (לא cache) ומחזיר `false` (לעולם לא זורק) אם ה-Preset נמחק או לא מכיל את ה-`ChannelType` - **אותו חוק בדיוק** כמו "פיקסצ'ר לא תואם" ב-`ApplyPresetCommand` (Step D), ממומש פעם אחת ב-`PresetLibrary.TryResolve` (שמממש `IPresetResolver`) ולא כפול.
- **`CueTiming`** (record: `FadeInTime`/`FadeOutTime`) בשלוש רמות precedence על `Cue`: `GeneralTiming` (כל ה-Cue, ברירת המחדל) → `AttributeTiming` (`Dictionary<AttributeClass, CueTiming>`, לדוגמה "לצבע תן 12 שניות") → `ChannelTiming` (פר-ערוץ בודד, קיים במודל אך לא בשימוש UI עדיין - "Individual Times" עתידי). `Cue.TimingFor(key, value)` פותר את הסדר הזה - זהה בדיוק להיררכיה של grandMA3 (General Cue Times → Feature Group Timing → Individual Attribute Timing).
- **`CueList.RecordCue`** (חתימה ציבורית ללא שינוי) ממשיך לסרוק את כל ה-Patch ולעטוף כל byte כ-`CueValue.Absolute` - **מדיניות ההקלטה ("full snapshot") לא השתנתה**, זו רק עטיפת טיפוס. **`RecordCueWithPresetRefs`** חדש (נוסף, לא מחליף) - מקבל `IReadOnlyDictionary<AttributeClass, Preset>` overrides, ומשתמש ב-`PresetRef` רק לערוצים שה-`AttributeClass`+`ChannelType` שלהם תואמים; שאר הערוצים נשמרים Absolute כרגיל - זה מוכיח ש-Cue מעורב Absolute+PresetRef נתמך במודל.
- **בנאי `CueList`** מקבל `IPresetResolver? presetResolver = null` אופציונלי (ברירת מחדל `null` - PresetRef אף פעם לא נפתר, נופל דרך ל-layer נמוך יותר; לא שובר קריאות קיימות). `MainViewModel` מחווט את ה-`PresetLibrary` האמיתי כ-resolver, כך שה-resolution עובד מקצה-לקצה כבר עכשיו למרות שאין UI ל"שמור עם preset" עדיין.
- **התנהגות כישלון מפורשת** (הוחלט כאן, המדריכים לא כיסו מקרה מחיקה): Preset מתעדכן → משתקף מיידית (אין cache). Preset נמחק / לא מכיל את ה-ChannelType → הערוץ לא תורם באותו tick, נופל ל-layer נמוך יותר, בלי exception. מחיקה **באמצע fade** פעיל → "חיתוך" (הערוץ מפסיק לתרום מיד), לא fade-out הדרגתי - החלטת עיצוב מתועדת, לא ניחוש שקט.
- **Tracking/ownership - לא מיושם, אבל מאומת שלא דורש rewrite**: `Cue.Levels` כבר `IReadOnlyDictionary` דליל - `RecordCue`'s "מלא תמיד" הוא מדיניות הקלטה, לא אילוץ מודל. Tracking עתידי (cache "ערך אחרון ידוע" ב-`CueList`, דגל Release פר-(cue,channel), הבחנת Blocked/Tracked) יתווסף **מעל** המודל הקיים בלי לשנות את `CueValue`/`Cue` - ו-`PresetRef` מתנהג בדיוק כמו `Absolute` מבחינת Tracking (אורתוגונלי לחלוטין).
- **בדיקות**: 6 חדשות ב-`tests/DmxConsole.Core.Tests/CueValueTests.cs` (mixed Absolute+PresetRef recording, resolution תקין, Preset נמחק/לא-תואם/מתעדכן באמצע show, timing precedence 3 רמות) + 2 שורות עדכון ב-`CueListTests.cs` הקיים (`.Levels[key]` → `.Levels[key].AbsoluteValue`). 125/125 ירוקות (היו 119). נבדק ידנית בדפדפן: הקלטת Cue + Go/Back/Stop ממשיכים לעבוד זהה - regression-only, כצפוי בשלב ללא UI חדש.

## Playback/Executor Architecture (Step F)

לפני התכנון הושקע מחקר+השוואה ייעודיים בין ארבע קונסולות (grandMA3/MagicQ/Compulite Vector/ETC Eos - ראה `docs/UX_PHILOSOPHY.md`) שהובילו למסקנה ש-`Executor = CueList + Fader + Flash` צר מדי: כל הקונסולות מפרידות **Playback Source** (מה משחקים) מ-**Handle** (הפקד) מ-**Master/Blend semantics** (Priority/MergePolicy/Flash). התוכנית עברה שלושה סבבי חיזוק נוספים מהמשתמש (הפרדת Undoable Commands מ-Operational Actions; ownership/merge/identity/tracking-readiness; structured Undo proposal + per-parameter revision granularity) לפני האישור הסופי.

- **`IPlaybackSource : IOutputLayer`** (`DmxConsole.Core.Engine`, חדש) - "משהו שניתן להצמיד ל-Executor", עם `GetStatus()` (introspection, time-first - ראה למטה). `ISequencedPlayback` (Go/Back/Stop) ו-`IPausablePlayback` (Pause/Resume/IsPaused) הן יכולות אופציונליות נפרדות. `CueList` מיישם את שלושתן + `GetStatus()` אמיתי (`CueListPlaybackStatus`/`TimingProgress` - Elapsed/Remaining/Total כשדות ראשיים, Percent מחושב-משני בלבד, לפי UX_PHILOSOPHY §9) + **Pause/Resume אמיתי** (מעקב `_accumulatedPause`, לא stub).
- **`Executor`** (`DmxConsole.Core.Engine`, חדש) - ה-handle: `Guid Id` יציב (זהות, לא תלוי ב-`Number`/`Name` הניתנים לשינוי), `FaderLevel`, `FlashMode` (`None/Add/Swap` - `Swap` מוחזר כ-**כישלון מפורש** דרך `TrySetFlash`, לא no-op שקט), `Source` הניתן ל-`Assign`/reassign בלי יצירה מחדש. **`ExecutorBank`** - אוסף פשוט, מקביל ל-`GroupManager`/`PresetLibrary`, `Number` לא ננעל ל-slot פיזי (משאיר מקום ל-Paging עתידי).
- **סמנטיקת fader**: Intensity מסולם פרופורציונלית (`raw * level`); Position/Color/Beam **לא** עוברים arithmetic על ה-byte הגולמי - הפאדר הוא on/off (level≤0 מדכא לגמרי, level>0 משאיר את הערך המלא) - מאושש ישירות מול המדריך של MagicQ ("LTP channels are not affected by the master faders"). הזיהוי "האם זה Intensity" מגיע מ-**`IChannelTypeLookup`** (`Patch` מיישם, לפי `FixtureChannel.Type` היציב) - **לא** מהאחסון הנוכחי של ה-source, כדי ש-Tracking עתידי (ערוץ tracked שלא מופיע ב-cue הנוכחי) ימשיך להיות מסווג נכון בלי rewrite.
- **Priority לפני MergePolicy, לכל policy כולל HTP** - תיקון ארכיטקטוני מרכזי ב-`DmxOutputEngine.ComputeUniverse`: two-pass per-channel (1: מוצא את ה-Priority הגבוה ביותר שתורם לערוץ הזה; 2: פותר **רק** בין התורמים בפריוריטי הזה, HTP=ערך מקסימלי, Ltp="Latest"=revision מקסימלי). זה שינוי סמנטי אמיתי ל-HTP הקיים (שהתעלם מ-Priority לגמרי) - מתועד במפורש, לא רגרסיה על בדיקות קיימות (אף בדיקה קיימת לא בדקה שני layers שונים ב-priority על אותו ערוץ HTP).
- **`IMergeAwareLayer.TryGetRevision(universe, channel, ...)`** - revision **פר-ערוץ**, לא גלובלי ל-Executor: `Executor` מפצל ל-`_intensityHandleRevision`/`_nonIntensityHandleRevision` (FaderLevel/Flash מעדכנים Intensity תמיד, ו-non-Intensity רק כשה-gate on/off באמת התהפך); `CueList` מחזיק `_channelRevisions` פר-(universe,channel) שמתעדכן ב-`StartTransitionTo` (כל הערוצים שה-cue החדש מכיל מקבלים אותה revision חדשה - נכון היום כי הקלטה היא full-snapshot, ונשאר נכון כש-Tracking יגיע ו-cues יהיו sparse). `Executor.TryGetRevision` = max(handle-revision-לפי-class, source's per-channel revision) - זה מה ש-Go שמשנה רק Color לא "גונב" בעלות על Intensity שלא נגעו בו.
- **`OutputOwner`** (`DmxConsole.Core.Engine`) - זהות מובנית (`Id` string, `Kind`, `ExecutorId: Guid?`, `ExecutorNumber: int?`) - **לא** string בלבד; `Executor.Id` (לא `Name`/`Number`) הוא מקור ה-Id, כך ששני Executors באותו Name נשארים מובחנים. `DmxOutputEngine.TryGetOwningLayer`/`GetOwner` - side effect זול של אותה לולאת מיזוג (בלי pass נוסף) - הבסיס העתידי ל-Select/Capture Active (יחד עם `IEffectiveOutputReader` הקיים מ-Step C1 ו-`PatchedFixture.ChannelsForAttribute` מ-Step A - שני החצאים כבר קיימים).
- **הפרדת `IConsoleCommand` (undoable) מ-`IConsoleAction`** (`DmxConsole.Application`, חדש) - Go/Back/Stop/Pause/Resume/FlashPress/FlashRelease הן Actions: עוברות דרך `CommandDispatcher.DispatchAction` בדיוק כמו Commands עוברות דרך `Dispatch`, מחזירות `CommandResult` מובנה, אבל **לעולם לא נוגעות ב-`UndoRedoService`** - זו ערובה מבנית (לא convention) ש-Undo history/Redo stack לא מושפעים מפעולות תפעוליות. `CueListViewModel`'s Go/Back/Stop עברו מקריאה ישירה ל-`CueList` ל-`DispatchAction` - אין יותר UI→Core bypass; `RecordCue`/`RemoveCue`/`GoToCue` נשארו ישירים (עריכת Cue היא future work).
- **`UndoProposal`/`UndoOption`/`IHasUndoRisk`** (`DmxConsole.Application`, חדש) - Undo כבר לא bool בודד: `UndoRedoService.PeekUndo()` חושף מה Undo יציע (רשימת אופציות, לא הפיכה אחת) לפני שמבצעים; `Undo(confirmedOptionId)` **אף פעם לא מבצע Destructive option בלי id מאושר מפורש** שחוזר בדיוק מ-`PeekUndo` קודם. `CreateExecutorCommand` (חדש), ובעקביות `CreateGroupCommand`/`StorePresetCommand`'s "was new" branch, מיישמים `IHasUndoRisk` → `Destructive` (Undo מוחק אובייקט שמור חדש) - כל שאר הפקודות (ברירת מחדל, בלי שינוי) נשארות `Safe`. Toolbar UI: כפתור Undo מציג prompt Confirm/Cancel כש-Undo נחסם.
- **Migration של ה-CueList הקיים**: `MainViewModel` בונה `ExecutorBank(channelTypeLookup: Patch)`, `Executor` מס' 1, `mainExecutor.Assign(cueList)`, ורושם את ה-**Executor** ל-`Engine.AddLayer` (לא את ה-`CueList` הגולמי) - עם `FaderLevel=1.0`/`Flash=None` ברירת מחדל זה byte-for-byte passthrough, אומת ב-regression tests ובבדיקה ידנית.
- **`Commands/Executors/`**: `AssignExecutorCommand`/`SetExecutorLevelCommand` (Safe) ו-`CreateExecutorCommand`/`RemoveExecutorCommand` (index-capture כמו Group). **`Commands/Playback/`**: שבע ה-Actions לעיל. `CommandResult` מקבל `Executor?`; `ConsoleActionType` מקבל את כל 11 הערכים החדשים (4 Commands + 7 Actions).
- UI: טאב רביעי **"Executors"** (`ExecutorPanel.razor`/`ExecutorViewModel`) - Create/Remove/Assign(-Unassign)/Level slider/Flash press-release. מינימלי בכוונה - proof-of-model, לא playback bank מלא.
- **נבדק ידנית בדפדפן**: regression מלא (Record/Go/Back/Stop זהים); Executor שני נוצר והוצמד לאותו CueList (מוכיח שמקור אחד יכול להיות מוצמד למספר Executors, כמו grandMA3); זרימת ה-Undo destructive-confirmation נבדקה קצה-לקצה (Delete Executor 2 → Undo → "⚠ Delete Executor 2 - delete it?" עם Confirm/Cancel → Cancel משאיר את ה-Executor קיים).
- **לא בשלב הזה** (במפורש): Pages, Speed/Rate Master, Swap flash מלא, Effect/Preset כ-source, Capture Active/Select Active בפועל, final CLI/NL syntax.
- **בדיקות**: ~40 חדשות (23 ב-`DmxConsole.Core.Tests`: `ExecutorTests`, `MergeTieBreakTests`, `OutputOwnershipTests` + תוספות ל-`CueListTests`; 14 ב-`DmxConsole.Application.Tests`: `ExecutorCommandTests`, `PlaybackActionTests`, `UndoRiskTests`; 2 בדיקות קיימות עודכנו לקרוא ל-Undo המובנה עם confirmedOptionId). 165/165 ירוקות (106 Core + 54 Application + 5 Protocols).

## הרחבה עתידית (שלבים הבאים)

כל שלב עתידי (Visualizer 3D, Music Sync) אמור להתחבר כ-`IOutputLayer`/`ITickable` נוסף ל-`DmxOutputEngine`, או כ-`IDmxSender` נוסף בפרויקט Protocols - בלי לשנות את הליבה הקיימת. Music Sync בפרט צפוי להזין `SpeedHz`/`Spread` של אפקטים קיימים לפי BPM שזוהה, ולא לדרוש סוג שכבה חדש.
