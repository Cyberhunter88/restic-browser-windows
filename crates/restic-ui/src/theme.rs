//! Zentrale Farben, Typografie und Schaltflächenstile (Gegenstück zu `App.axaml` und `App.SetTheme`).
//! Farben stehen ausschließlich hier; Fenster und Dialoge holen sie über [`Theme::palette`].

use eframe::egui::{self, Color32, CornerRadius, RichText, Stroke, SystemTheme, ViewportCommand};

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Theme {
    Light,
    Dark,
}

#[derive(Clone, Copy, Debug)]
pub struct Palette {
    pub background: Color32,
    pub panel: Color32,
    pub row_even: Color32,
    pub row_odd: Color32,
    pub hover: Color32,
    pub selected: Color32,
    pub text: Color32,
    pub secondary_text: Color32,
    pub divider: Color32,
    pub accent: Color32,
    pub on_accent: Color32,
    pub danger: Color32,
    pub folder: Color32,
}

impl Theme {
    pub fn is_dark(self) -> bool {
        self == Theme::Dark
    }

    pub fn toggled(self) -> Self {
        if self.is_dark() { Theme::Light } else { Theme::Dark }
    }

    pub fn palette(self) -> Palette {
        match self {
            Theme::Dark => Palette {
                background: Color32::from_rgb(0x1c, 0x1c, 0x1c),
                panel: Color32::from_rgb(0x25, 0x25, 0x26),
                row_even: Color32::from_rgb(0x1f, 0x1f, 0x1f),
                row_odd: Color32::from_rgb(0x24, 0x24, 0x25),
                hover: Color32::from_rgb(0x2f, 0x3a, 0x45),
                selected: Color32::from_rgb(0x1f, 0x44, 0x68),
                text: Color32::from_rgb(0xe6, 0xe6, 0xe6),
                secondary_text: Color32::from_rgb(0xa0, 0xa0, 0xa0),
                divider: Color32::from_rgb(0x3c, 0x3c, 0x3c),
                accent: Color32::from_rgb(0x60, 0xcd, 0xff),
                on_accent: Color32::from_rgb(0x00, 0x00, 0x00),
                danger: Color32::from_rgb(0xff, 0x99, 0xa4),
                folder: Color32::from_rgb(0xf0, 0xc0, 0x5a),
            },
            Theme::Light => Palette {
                background: Color32::from_rgb(0xfa, 0xfa, 0xfa),
                panel: Color32::from_rgb(0xf0, 0xf0, 0xf0),
                row_even: Color32::from_rgb(0xff, 0xff, 0xff),
                row_odd: Color32::from_rgb(0xf8, 0xf8, 0xf8),
                hover: Color32::from_rgb(0xe8, 0xf0, 0xf8),
                selected: Color32::from_rgb(0xcc, 0xe4, 0xf7),
                text: Color32::from_rgb(0x1a, 0x1a, 0x1a),
                secondary_text: Color32::from_rgb(0x5f, 0x5f, 0x5f),
                divider: Color32::from_rgb(0xdc, 0xdc, 0xdc),
                accent: Color32::from_rgb(0x00, 0x67, 0xc0),
                on_accent: Color32::from_rgb(0xff, 0xff, 0xff),
                danger: Color32::from_rgb(0xc4, 0x2b, 0x1c),
                folder: Color32::from_rgb(0xe8, 0xb6, 0x4c),
            },
        }
    }
}

/// Wendet Farben, Abstände und kurze Animationen an und setzt die Titelleiste des Betriebssystems.
///
/// Die Titelleiste lässt sich erst setzen, wenn das Fenster existiert; die Aufrufer wiederholen den
/// Aufruf deshalb nach den ersten Bildern (siehe `app.rs`).
pub fn apply(ctx: &egui::Context, theme: Theme) {
    let palette = theme.palette();
    let mut visuals = if theme.is_dark() {
        egui::Visuals::dark()
    } else {
        egui::Visuals::light()
    };
    visuals.panel_fill = palette.background;
    visuals.window_fill = palette.background;
    visuals.extreme_bg_color = palette.row_even;
    visuals.faint_bg_color = palette.row_odd;
    visuals.override_text_color = Some(palette.text);
    visuals.selection.bg_fill = palette.selected;
    visuals.selection.stroke = Stroke::new(1.0, palette.text);
    visuals.hyperlink_color = palette.accent;
    let radius = CornerRadius::same(4);
    for widget in [
        &mut visuals.widgets.noninteractive,
        &mut visuals.widgets.inactive,
        &mut visuals.widgets.hovered,
        &mut visuals.widgets.active,
        &mut visuals.widgets.open,
    ] {
        widget.corner_radius = radius;
    }
    visuals.widgets.inactive.weak_bg_fill = palette.panel;
    visuals.widgets.hovered.weak_bg_fill = palette.hover;
    visuals.widgets.noninteractive.bg_stroke = Stroke::new(1.0, palette.divider);
    visuals.window_stroke = Stroke::new(1.0, palette.divider);
    ctx.set_visuals(visuals);

    ctx.global_style_mut(|style| {
        style.animation_time = 0.12; // dezent: 100 bis 200 ms
        style.spacing.item_spacing = egui::vec2(8.0, 6.0);
        style.spacing.button_padding = egui::vec2(12.0, 5.0);
        style.spacing.interact_size.y = 28.0;
        use egui::{FontFamily::Proportional, FontId, TextStyle};
        style
            .text_styles
            .insert(TextStyle::Body, FontId::new(14.0, Proportional));
        style
            .text_styles
            .insert(TextStyle::Button, FontId::new(14.0, Proportional));
        style
            .text_styles
            .insert(TextStyle::Small, FontId::new(12.0, Proportional));
        style
            .text_styles
            .insert(TextStyle::Heading, FontId::new(20.0, Proportional));
    });

    ctx.send_viewport_cmd(ViewportCommand::SetTheme(if theme.is_dark() {
        SystemTheme::Dark
    } else {
        SystemTheme::Light
    }));
}

// --- Gemeinsame Textstile (title, sectionTitle, secondary, small) ---

pub fn title(text: impl Into<String>) -> RichText {
    RichText::new(text).size(20.0).strong()
}

pub fn section_title(text: impl Into<String>) -> RichText {
    RichText::new(text).size(15.0).strong()
}

pub fn secondary(palette: &Palette, text: impl Into<String>) -> RichText {
    RichText::new(text).color(palette.secondary_text)
}

pub fn small(palette: &Palette, text: impl Into<String>) -> RichText {
    RichText::new(text).size(12.0).color(palette.secondary_text)
}

// --- Schaltflächenstile (primary, quiet) ---

/// Hervorgehobene Hauptaktion, z. B. „Wiederherstellen“.
pub fn primary_button(palette: &Palette, text: impl Into<String>) -> egui::Button<'static> {
    egui::Button::new(RichText::new(text).color(palette.on_accent)).fill(palette.accent)
}

/// Zurückhaltende Schaltfläche ohne Rahmen für seltenere Aktionen.
pub fn quiet_button(text: impl Into<String>) -> egui::Button<'static> {
    egui::Button::new(RichText::new(text)).frame(false)
}

/// Gemeinsamer Rahmen aller Dialoge: gleiche Abstände und Ränder in Hell und Dunkel.
pub fn dialog_frame(ctx: &egui::Context) -> egui::Frame {
    egui::Frame::popup(&ctx.global_style()).inner_margin(egui::Margin::same(20))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn palettes_differ_and_keep_text_readable() {
        let (light, dark) = (Theme::Light.palette(), Theme::Dark.palette());
        assert_ne!(light.background, dark.background);
        let luminance =
            |c: Color32| 0.2126 * f32::from(c.r()) + 0.7152 * f32::from(c.g()) + 0.0722 * f32::from(c.b());
        assert!(
            luminance(light.text) < 80.0 && luminance(light.background) > 200.0,
            "heller Modus: dunkler Text auf hellem Grund"
        );
        assert!(
            luminance(dark.text) > 180.0 && luminance(dark.background) < 60.0,
            "dunkler Modus: heller Text auf dunklem Grund"
        );
        for palette in [light, dark] {
            assert!(
                (luminance(palette.accent) - luminance(palette.on_accent)).abs() > 100.0,
                "Text auf der Akzentfarbe muss lesbar sein"
            );
        }
    }

    #[test]
    fn toggling_switches_between_the_two_themes() {
        assert_eq!(Theme::Light.toggled(), Theme::Dark);
        assert_eq!(Theme::Dark.toggled(), Theme::Light);
    }
}
