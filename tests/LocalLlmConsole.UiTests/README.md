# WPF test suite structure

This project exercises compiled WPF surfaces on one shared STA dispatcher.
Tests are grouped by the surface they compose:

| Folder | Scope |
| --- | --- |
| `Shell` | main-window composition, navigation, localization, accessibility, and dialogs |
| `Surfaces` | independently composed cross-feature page smoke tests |
| `Settings` | launch settings, application settings, and Help surfaces |
| `Models` | model, profile, group, and row-action surfaces |
| `Overview` | dashboard, session actions, retention, resizing, and customization |
| `Runtime` | live runtime-log behavior |
| `Lifetime` | historical usage and energy surfaces |
| `TestSupport` | shared STA dispatcher, visual-tree helpers, fixtures, and assertions |

Keep these tests focused on rendered controls, bindings, resources, accessibility,
and interaction behavior. Pure policies and application services belong in the
main `LocalLlmConsole.Tests` project so the WPF suite remains small and reliable.
