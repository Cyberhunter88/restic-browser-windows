//! Datei- und Ordnersymbole sowie Pfeile als Vektorgrafik. Keine Symbolschrift nötig: die Standardschrift
//! von egui enthält keine Emoji, und eigene Formen bleiben in Hell und Dunkel scharf und lizenzfrei.

use eframe::egui::{Color32, CornerRadius, Painter, Pos2, Rect, Response, Sense, Shape, Stroke, Ui, vec2};

use crate::theme::Palette;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum IconKind {
    Folder,
    File,
    Image,
    Document,
    Text,
    Archive,
    Link,
}

/// Wählt das Symbol nach Knotentyp und Dateiendung.
pub fn icon_kind(node_type: &str, name: &str) -> IconKind {
    match node_type {
        "dir" => IconKind::Folder,
        "symlink" => IconKind::Link,
        _ => {
            let extension = name
                .rsplit_once('.')
                .map(|(_, ext)| ext.to_lowercase())
                .unwrap_or_default();
            match extension.as_str() {
                "png" | "jpg" | "jpeg" | "bmp" | "gif" | "ico" | "webp" | "svg" | "tif" | "tiff" | "heic" => {
                    IconKind::Image
                }
                "pdf" | "doc" | "docx" | "xls" | "xlsx" | "ppt" | "pptx" | "odt" | "ods" | "rtf" => {
                    IconKind::Document
                }
                "txt" | "md" | "json" | "xml" | "log" | "ini" | "yaml" | "yml" | "csv" => IconKind::Text,
                "zip" | "tar" | "gz" | "7z" | "rar" | "bz2" | "xz" | "iso" => IconKind::Archive,
                _ => IconKind::File,
            }
        }
    }
}

fn kind_color(kind: IconKind, palette: &Palette) -> Color32 {
    match kind {
        IconKind::Folder => palette.folder,
        IconKind::File => Color32::from_rgb(0x8a, 0x9b, 0xb0),
        IconKind::Image => Color32::from_rgb(0x4c, 0xaf, 0x7a),
        IconKind::Document => Color32::from_rgb(0xe0, 0x6b, 0x6b),
        IconKind::Text => Color32::from_rgb(0x7f, 0x9c, 0xc8),
        IconKind::Archive => Color32::from_rgb(0xb0, 0x8a, 0x58),
        IconKind::Link => palette.accent,
    }
}

/// Zeichnet das Symbol in `rect` (quadratisch gedacht).
pub fn paint_icon(painter: &Painter, rect: Rect, kind: IconKind, palette: &Palette) {
    let color = kind_color(kind, palette);
    let (w, h) = (rect.width(), rect.height());
    let at = |x: f32, y: f32| Pos2::new(rect.left() + w * x, rect.top() + h * y);
    if kind == IconKind::Folder {
        painter.rect_filled(
            Rect::from_min_max(at(0.06, 0.18), at(0.44, 0.42)),
            CornerRadius::same(2),
            color.gamma_multiply(0.85),
        );
        painter.rect_filled(
            Rect::from_min_max(at(0.06, 0.28), at(0.94, 0.84)),
            CornerRadius::same(2),
            color,
        );
        return;
    }
    let fold = w * 0.28;
    let page = vec![
        at(0.18, 0.06),
        Pos2::new(rect.left() + w * 0.82 - fold, rect.top() + h * 0.06),
        at(0.82, 0.06 + 0.28 * w / h),
        at(0.82, 0.94),
        at(0.18, 0.94),
    ];
    painter.add(Shape::convex_polygon(page, color, Stroke::NONE));
    painter.add(Shape::convex_polygon(
        vec![
            Pos2::new(rect.left() + w * 0.82 - fold, rect.top() + h * 0.06),
            at(0.82, 0.06 + 0.28 * w / h),
            Pos2::new(rect.left() + w * 0.82 - fold, rect.top() + h * 0.06 + fold),
        ],
        Color32::from_black_alpha(60),
        Stroke::NONE,
    ));
    let line = Stroke::new(1.2, Color32::from_white_alpha(170));
    match kind {
        IconKind::Image => {
            painter.add(Shape::convex_polygon(
                vec![
                    at(0.26, 0.82),
                    at(0.46, 0.5),
                    at(0.62, 0.72),
                    at(0.72, 0.6),
                    at(0.76, 0.82),
                ],
                Color32::from_white_alpha(190),
                Stroke::NONE,
            ));
        }
        IconKind::Archive => {
            painter.line_segment(
                [at(0.5, 0.3), at(0.5, 0.86)],
                Stroke::new(2.0, Color32::from_white_alpha(190)),
            );
        }
        IconKind::Link => {
            painter.line_segment(
                [at(0.3, 0.7), at(0.68, 0.38)],
                Stroke::new(1.8, Color32::from_white_alpha(220)),
            );
            painter.line_segment(
                [at(0.5, 0.38), at(0.68, 0.38)],
                Stroke::new(1.8, Color32::from_white_alpha(220)),
            );
            painter.line_segment(
                [at(0.68, 0.38), at(0.68, 0.56)],
                Stroke::new(1.8, Color32::from_white_alpha(220)),
            );
        }
        IconKind::Document | IconKind::Text | IconKind::File => {
            for y in [0.46, 0.62, 0.78] {
                painter.line_segment([at(0.3, y), at(0.7, y)], line);
            }
        }
        IconKind::Folder => {}
    }
}

/// Reserviert 18 × 18 Punkte und zeichnet das Symbol.
pub fn icon(ui: &mut Ui, kind: IconKind, palette: &Palette) -> Response {
    let (rect, response) = ui.allocate_exact_size(vec2(18.0, 18.0), Sense::hover());
    if ui.is_rect_visible(rect) {
        paint_icon(ui.painter(), rect, kind, palette);
    }
    response
}

/// Kleines Dreieck für Sortierrichtung und Auf-/Zuklappen.
pub fn paint_arrow(painter: &Painter, center: Pos2, up: bool, color: Color32) {
    let (half, height) = (4.5, 3.0);
    let (base_y, tip_y) = if up {
        (center.y + height, center.y - height)
    } else {
        (center.y - height, center.y + height)
    };
    painter.add(Shape::convex_polygon(
        vec![
            Pos2::new(center.x - half, base_y),
            Pos2::new(center.x + half, base_y),
            Pos2::new(center.x, tip_y),
        ],
        color,
        Stroke::NONE,
    ));
}

/// Pfeil als eigenes Element in der Zeile (für Kopfzeilen und Schaltflächen).
pub fn arrow(ui: &mut Ui, up: bool, color: Color32) -> Response {
    let (rect, response) = ui.allocate_exact_size(vec2(12.0, 12.0), Sense::hover());
    paint_arrow(ui.painter(), rect.center(), up, color);
    response
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn kinds_follow_type_and_extension() {
        assert_eq!(icon_kind("dir", "docs"), IconKind::Folder);
        assert_eq!(icon_kind("symlink", "link"), IconKind::Link);
        assert_eq!(icon_kind("file", "Foto.JPG"), IconKind::Image);
        assert_eq!(icon_kind("file", "bericht.pdf"), IconKind::Document);
        assert_eq!(icon_kind("file", "notizen.md"), IconKind::Text);
        assert_eq!(icon_kind("file", "backup.tar"), IconKind::Archive);
        assert_eq!(icon_kind("file", "programm.exe"), IconKind::File);
        assert_eq!(icon_kind("file", "ohne-endung"), IconKind::File);
        assert_eq!(icon_kind("", ".bashrc"), IconKind::File);
    }
}
