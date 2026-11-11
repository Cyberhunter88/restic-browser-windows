//! Zeichnung der Dateitabelle: virtualisierte Zeilen, Mehrfachauswahl-Darstellung, sortierbare Köpfe
//! und unsichtbare, aber ziehbare Spaltenanfasser.

use eframe::egui::{self, Align, Layout, RichText, Sense, Stroke, Ui};
use egui_extras::{Column, TableBuilder};
use restic_core::models::BackupNode;

use crate::icons;
use crate::table_model::{SortColumn, TableModel};
use crate::theme::{self, Palette};

/// Was der Nutzer in diesem Bild getan hat; wird nach dem Zeichnen angewendet.
#[derive(Default)]
pub struct TableOutput {
    /// Angeklickte Zeile (Anzeigezeile) mit Strg und Umschalt.
    pub clicked: Option<(usize, bool, bool)>,
    /// Doppelklick auf eine Zeile (Ordner öffnen).
    pub activated: Option<usize>,
    /// Klick auf einen Spaltenkopf.
    pub sort: Option<SortColumn>,
    /// Gewählter Eintrag des Kontextmenüs (wirkt auf die aktuelle Auswahl).
    pub menu: Option<RowAction>,
    /// Die Spalte „Berechtigungen“ soll ein- oder ausgeblendet werden.
    pub toggle_permissions: bool,
}

/// Einträge des Kontextmenüs einer Zeile.
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum RowAction {
    Open,
    Preview,
    Versions,
    Restore,
    /// Pfade der Auswahl in die Zwischenablage kopieren.
    CopyPath,
    /// Den Ordner eines Suchtreffers öffnen.
    OpenParent,
}

/// Welche Menüeinträge für die aktuelle Auswahl verfügbar sind.
#[derive(Clone, Copy, Default)]
pub struct MenuState {
    pub can_open: bool,
    pub can_preview: bool,
    pub can_restore: bool,
    /// Nur in Suchergebnissen mit genau einem ausgewählten Treffer.
    pub can_open_parent: bool,
    pub show_permissions: bool,
}

fn permissions_entry(ui: &mut Ui, shown: bool, output: &mut TableOutput) {
    let mut checked = shown;
    if ui.checkbox(&mut checked, "Spalte „Berechtigungen“").clicked() {
        output.toggle_permissions = true;
        ui.close();
    }
}

pub fn format_modified(node: &BackupNode) -> String {
    node.modified
        .map(|time| time.format("%d.%m.%Y %H:%M").to_string())
        .unwrap_or_default()
}

pub fn show(
    ui: &mut Ui,
    model: &TableModel,
    nodes: &[BackupNode],
    palette: &Palette,
    scroll_to: Option<usize>,
    menu: MenuState,
) -> TableOutput {
    let mut output = TableOutput::default();
    let modifiers = ui.input(|input| input.modifiers);
    ui.scope(|ui| {
        // Die Anfasser zeichnet egui_extras mit diesen Strichen. Ohne Strich bleiben sie unsichtbar,
        // lassen sich aber weiterhin greifen und ziehen (Regel aus AGENTS.md).
        let widgets = &mut ui.visuals_mut().widgets;
        widgets.noninteractive.bg_stroke = Stroke::NONE;
        widgets.hovered.bg_stroke = Stroke::NONE;
        widgets.active.bg_stroke = Stroke::NONE;

        let mut table = TableBuilder::new(ui)
            .striped(true)
            .resizable(true)
            .sense(Sense::click())
            .column(Column::initial(380.0).at_least(80.0).clip(true))
            .column(Column::initial(110.0).at_least(60.0).clip(true))
            .column(Column::initial(100.0).at_least(60.0).clip(true));
        if menu.show_permissions {
            // Eigene Kennung: Die gespeicherten Spaltenbreiten gelten je Spaltenzahl.
            table = table
                .id_salt("dateien-mit-rechten")
                .column(Column::initial(110.0).at_least(80.0).clip(true));
        }
        table = table.column(Column::remainder().at_least(110.0).clip(true));
        if let Some(row) = scroll_to {
            table = table.scroll_to_row(row, None);
        }
        table
            .header(30.0, |mut header| {
                let mut columns = vec![
                    ("Name", Some(SortColumn::Name)),
                    ("Typ", Some(SortColumn::Kind)),
                    ("Größe", Some(SortColumn::Size)),
                ];
                if menu.show_permissions {
                    columns.push(("Berechtigungen", None));
                }
                columns.push(("Geändert", Some(SortColumn::Modified)));
                for (label, column) in columns {
                    header.col(|ui| {
                        ui.horizontal(|ui| {
                            let response = ui.add(
                                egui::Label::new(theme::small(palette, label).strong()).sense(Sense::click()),
                            );
                            if column == Some(model.sort_column()) {
                                icons::arrow(ui, model.is_ascending(), palette.secondary_text);
                            }
                            if response.clicked() && column.is_some() {
                                output.sort = column;
                            }
                            response.context_menu(|ui| {
                                permissions_entry(ui, menu.show_permissions, &mut output);
                            });
                        });
                    });
                }
            })
            .body(|body| {
                body.rows(28.0, model.len(), |mut row| {
                    let display = row.index();
                    // Die Tabelle kann der Knotenliste kurz hinterherhinken; unbekannte Indizes werden übersprungen.
                    let Some(node) = nodes.get(model.node_index(display)) else {
                        return;
                    };
                    row.set_selected(model.is_row_selected(display));
                    row.col(|ui| {
                        ui.horizontal(|ui| {
                            // Suchtreffer haben nur einen Pfad; der Anzeigename ist dann das letzte Pfadstück.
                            let name = node.display_name();
                            icons::icon(ui, icons::icon_kind(&node.node_type, name), palette);
                            let text = if node.is_directory() {
                                RichText::new(name).strong()
                            } else {
                                RichText::new(name)
                            };
                            if node.name.is_empty() {
                                // Treffer stammen aus verschiedenen Ordnern: den Ort hinter dem Namen zeigen.
                                ui.label(text);
                                let parent = restic_core::command::parent_path(&node.path);
                                ui.add(egui::Label::new(theme::small(palette, parent)).truncate());
                            } else {
                                ui.add(egui::Label::new(text).truncate());
                            }
                        });
                    });
                    row.col(|ui| {
                        ui.with_layout(Layout::left_to_right(Align::Center), |ui| {
                            ui.label(theme::secondary(palette, node.type_text()));
                        });
                    });
                    row.col(|ui| {
                        ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                            ui.label(theme::secondary(
                                palette,
                                if node.is_directory() {
                                    String::new()
                                } else {
                                    node.size_text()
                                },
                            ));
                        });
                    });
                    if menu.show_permissions {
                        row.col(|ui| {
                            ui.with_layout(Layout::left_to_right(Align::Center), |ui| {
                                ui.label(theme::secondary(palette, node.permissions.as_str()).monospace());
                            });
                        });
                    }
                    row.col(|ui| {
                        ui.with_layout(Layout::left_to_right(Align::Center), |ui| {
                            ui.label(theme::secondary(palette, format_modified(node)));
                        });
                    });
                    let response = row.response();
                    if response.secondary_clicked() && !model.is_row_selected(display) {
                        // Rechtsklick auf eine nicht ausgewählte Zeile wählt zuerst diese Zeile.
                        output.clicked = Some((display, false, false));
                    }
                    response.context_menu(|ui| {
                        let entries = [
                            ("Öffnen", RowAction::Open, menu.can_open),
                            ("Vorschau", RowAction::Preview, menu.can_preview),
                            ("Dateiversionen", RowAction::Versions, menu.can_preview),
                            ("Wiederherstellen …", RowAction::Restore, menu.can_restore),
                            ("Pfad kopieren", RowAction::CopyPath, menu.can_restore),
                            (
                                "Ordner des Treffers öffnen",
                                RowAction::OpenParent,
                                menu.can_open_parent,
                            ),
                        ];
                        for (label, action, enabled) in entries {
                            // Der Eintrag für Suchtreffer erscheint nur dort, wo er etwas bewirkt.
                            if action == RowAction::OpenParent && !enabled {
                                continue;
                            }
                            if ui.add_enabled(enabled, egui::Button::new(label)).clicked() {
                                output.menu = Some(action);
                                ui.close();
                            }
                        }
                        ui.separator();
                        permissions_entry(ui, menu.show_permissions, &mut output);
                    });
                    if response.double_clicked() {
                        output.activated = Some(display);
                    } else if response.clicked() {
                        output.clicked = Some((display, modifiers.command, modifiers.shift));
                    }
                });
            });
    });
    output
}
