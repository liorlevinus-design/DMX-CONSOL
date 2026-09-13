# ארכיטקטורה — DMX Console

## מבנה הפרויקטים

- **DmxConsole.Core** — מנוע ליבה ללא תלות ב-UI או ברשת: `Universe`, `Fixtures` (Profile/Mode/Channel/PatchedFixture/Patch), ו-`Engine` (Programmer, IOutputLayer, DmxOutputEngine).
- **DmxConsole.Protocols** — שולחי DMX על גבי רשת: `ArtNetSender` (Art-Net/ArtDMX על UDP broadcast/unicast), `SacnSender` (E1.31 על UDP multicast).
- **DmxConsole.Fixtures** — ספריית פרופילי פיקסצ'רים. כרגע `GenericFixtureLibrary` עם כמה טיפוסים גנריים; בהמשך ייתכן טעינת JSON/GDTF.
- **DmxConsole.App** — אפליקציית WPF (MVVM עם CommunityToolkit.Mvvm): Patch view + Fader bank.
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
- ב-App: `CueListViewModel` עוטף את זה, כולל `DispatcherTimer` שסוקר כל 100ms כדי לעדכן progress bar/זמן נותר (כי `CueList.Changed` נורה רק בפעולות בדידות, לא ברציפות תוך כדי fade).

## הרחבה עתידית (שלבים הבאים)

כל שלב עתידי (Effects, USB-DMX, Visualizer 3D, Music Sync) אמור להתחבר כ-`IOutputLayer` נוסף ל-`DmxOutputEngine` (Effects בעדיפות גבוהה מ-CueList אך נמוכה מ-Programmer), או כ-`IDmxSender` נוסף בפרויקט Protocols - בלי לשנות את הליבה הקיימת.
