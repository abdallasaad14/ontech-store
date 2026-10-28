# OnTech Store: rules for every change

## The desktop app must work in Arabic (RTL) and English
Every change to `src/OnTech.Store.Desktop` must look and work right in both languages, including the right-to-left Arabic layout.

- **No hard-coded UI text.** In XAML write `{l:T 'English text'}`. In C# write `L.T("English text")`, or `L.T("... {0} ...", value)` when the text has values in it. Add the Arabic for every new text to `Services/Arabic.cs`, using the same `{0}` placeholders.
- **Messages from the server.** When you add a new server error message, add its Arabic to `Arabic.cs`: as an exact-text entry, or in `Patterns` when the message contains names or numbers.
- **Layout.** Windows already set `FlowDirection` through `L.Apply(this)`, so call it in every new window's constructor. Let WPF mirror the layout. Use margins, `DockPanel.Dock` and `HorizontalAlignment`; never use absolute positions that assume left-to-right. Images and logos that must not flip get `FlowDirection="LeftToRight"`.
- **Choices in lists.** Never compare against English display text (combo boxes, filters). Use an index or an enum, and show it through `L.T` or the `Translate` converter.
- **Message boxes.** Go through `Dialogs`, or pass `L.MessageBoxOptions`.
- **XAML escaping.** A quoted `{l:T}` value that starts with `{` needs escaped braces, for example `{l:T '\{0\} available'}`.
- **Before pushing.** Build the app. Check that every new `L.T` or `{l:T}` text has an Arabic entry. Say in the PR how the change looks in Arabic.
