//! UI-Spike: Dateitabelle mit 100.000 Zeilen, Mehrfachauswahl, Sortierung, Hell/Dunkel,
//! dunkler Titelleiste und Screenshot-Modus (`--screenshot datei.png`).
//!
//! Aufruf: `slint-ui-spike [--dark] [--rows N] [--select von bis] [--hover x y] [--loading]
//!          [--popup] [--sort spalte] [--screenshot pfad]`

use std::cell::RefCell;
use std::rc::Rc;
use std::time::{Duration, Instant};

use slint::winit_030::{WinitWindowAccessor, winit};
use slint::{ComponentHandle, Model, ModelRc, SharedString, VecModel};

slint::include_modules!();

struct Item {
    name: String,
    is_dir: bool,
    size: u64,
    modified: i64, // Minuten seit 2020-01-01
    selected: bool,
}

struct State {
    items: Vec<Item>,
    anchor: usize,
    cursor: usize,
    sort_column: i32,
    ascending: bool,
}

fn format_size(bytes: u64) -> String {
    const UNITS: [&str; 5] = ["B", "KB", "MB", "GB", "TB"];
    let mut value = bytes as f64;
    let mut unit = 0;
    while value >= 1024.0 && unit < UNITS.len() - 1 {
        value /= 1024.0;
        unit += 1;
    }
    let text = format!("{value:.1}");
    format!("{} {}", text.strip_suffix(".0").unwrap_or(&text).replace('.', ","), UNITS[unit])
}

fn format_time(minutes: i64) -> String {
    let days = minutes / (24 * 60);
    let (hour, minute) = ((minutes / 60) % 24, minutes % 60);
    let year = 2020 + days / 365;
    let day_of_year = days % 365;
    format!("{:02}.{:02}.{year} {hour:02}:{minute:02}", day_of_year % 28 + 1, day_of_year / 28 % 12 + 1)
}

fn icon_for(item: &Item) -> &'static str {
    if item.is_dir {
        return "📁";
    }
    match item.name.rsplit('.').next().unwrap_or_default() {
        "png" | "jpg" => "🖼",
        "pdf" => "📕",
        "txt" | "md" => "📝",
        "zip" | "gz" => "📦",
        _ => "📄",
    }
}

fn kind_for(item: &Item) -> &'static str {
    if item.is_dir {
        "Ordner"
    } else {
        "Datei"
    }
}

fn to_row(item: &Item) -> FileRow {
    FileRow {
        icon: icon_for(item).into(),
        name: item.name.as_str().into(),
        kind: kind_for(item).into(),
        size: if item.is_dir { "—".into() } else { format_size(item.size).into() },
        modified: format_time(item.modified).into(),
        is_dir: item.is_dir,
        selected: item.selected,
    }
}

fn sample_items(count: usize) -> Vec<Item> {
    let extensions = ["txt", "pdf", "png", "zip", "md", "jpg", "dat", "gz"];
    (0..count)
        .map(|i| {
            let is_dir = i % 25 == 0;
            let name = if is_dir {
                format!("Ordner {i:05}")
            } else {
                format!("Datei mit längerem Namen {i:06}.{}", extensions[i % extensions.len()])
            };
            Item {
                name,
                is_dir,
                size: ((i as u64 * 7919) % 5_000_000) + 12,
                modified: ((i as i64 * 104_729) % 2_000_000) + 1,
                selected: false,
            }
        })
        .collect()
}

fn sort_items(state: &mut State) {
    let (column, ascending) = (state.sort_column, state.ascending);
    state.items.sort_by(|a, b| {
        let ordering = match column {
            1 => kind_for(a).cmp(kind_for(b)),
            2 => a.size.cmp(&b.size),
            3 => a.modified.cmp(&b.modified),
            _ => a.name.to_lowercase().cmp(&b.name.to_lowercase()),
        };
        // Ordner stehen immer zuerst.
        b.is_dir.cmp(&a.is_dir).then(if ascending { ordering } else { ordering.reverse() })
    });
    state.anchor = 0;
    state.cursor = 0;
}

fn refresh_summary(window: &MainWindow, state: &State) {
    let selected: Vec<&Item> = state.items.iter().filter(|i| i.selected).collect();
    let count = selected.len();
    let folders = selected.iter().filter(|i| i.is_dir).count();
    let bytes: u64 = selected.iter().filter(|i| !i.is_dir).map(|i| i.size).sum();
    window.set_selected_count(count as i32);
    window.set_selection_summary(
        match count {
            0 => "Nichts ausgewählt".to_string(),
            _ => format!("{} Element(e) ausgewählt ({folders} Ordner, {})", count, format_size(bytes)),
        }
        .into(),
    );
}

fn set_range(state: &mut State, model: &VecModel<FileRow>, from: usize, to: usize, keep_others: bool) {
    let (low, high) = (from.min(to), from.max(to));
    for (index, item) in state.items.iter_mut().enumerate() {
        let want = (low..=high).contains(&index) || (keep_others && item.selected);
        if item.selected != want {
            item.selected = want;
            model.set_row_data(index, to_row(item));
        }
    }
}

fn apply_theme(window: &MainWindow, dark: bool) {
    // Beide Ebenen umschalten: die Palette der Widgets und die Titelleiste des Betriebssystems.
    window.set_is_dark(dark);
    let applied = window.window().with_winit_window(|w| {
        w.set_theme(Some(if dark { winit::window::Theme::Dark } else { winit::window::Theme::Light }));
        w.theme()
    });
    eprintln!("THEME dark={dark} winit-fenster-vorhanden={} effektiv={applied:?}", applied.is_some());
}

struct Options {
    dark: bool,
    rows: usize,
    select: Option<(usize, usize)>,
    hover: Option<(f32, f32)>,
    click: Option<(f32, f32)>,
    loading: bool,
    popup: bool,
    sort: Option<i32>,
    screenshot: Option<String>,
    bench: bool,
    dialog: bool,
    hold: Option<u64>,
}

fn parse_options() -> Options {
    let args: Vec<String> = std::env::args().skip(1).collect();
    let mut options = Options { dark: false, rows: 100_000, select: None, hover: None, click: None, loading: false, popup: false, sort: None, screenshot: None, bench: false, dialog: false, hold: None };
    let mut i = 0;
    while i < args.len() {
        match args[i].as_str() {
            "--dark" => options.dark = true,
            "--loading" => options.loading = true,
            "--bench" => options.bench = true,
            "--dialog" => options.dialog = true,
            "--hold" => {
                options.hold = args[i + 1].parse().ok();
                i += 1;
            }
            "--popup" => options.popup = true,
            "--rows" => {
                options.rows = args[i + 1].parse().unwrap_or(100_000);
                i += 1;
            }
            "--select" => {
                options.select = Some((args[i + 1].parse().unwrap_or(0), args[i + 2].parse().unwrap_or(0)));
                i += 2;
            }
            "--hover" => {
                options.hover = Some((args[i + 1].parse().unwrap_or(0.0), args[i + 2].parse().unwrap_or(0.0)));
                i += 2;
            }
            "--click" => {
                options.click = Some((args[i + 1].parse().unwrap_or(0.0), args[i + 2].parse().unwrap_or(0.0)));
                i += 2;
            }
            "--sort" => {
                options.sort = args[i + 1].parse().ok();
                i += 1;
            }
            "--screenshot" => {
                options.screenshot = Some(args[i + 1].clone());
                i += 1;
            }
            _ => {}
        }
        i += 1;
    }
    options
}

fn save_png(window: &MainWindow, path: &str) {
    match window.window().take_snapshot() {
        Ok(buffer) => {
            let file = std::fs::File::create(path).expect("Screenshot-Datei");
            let mut encoder = png::Encoder::new(std::io::BufWriter::new(file), buffer.width(), buffer.height());
            encoder.set_color(png::ColorType::Rgba);
            encoder.set_depth(png::BitDepth::Eight);
            let mut writer = encoder.write_header().expect("PNG-Kopf");
            writer.write_image_data(buffer.as_bytes()).expect("PNG-Daten");
            println!("Screenshot: {path} ({}x{})", buffer.width(), buffer.height());
        }
        Err(error) => eprintln!("Screenshot nicht möglich: {error}"),
    }
}

fn main() -> Result<(), slint::PlatformError> {
    let options = parse_options();
    let window = MainWindow::new()?;

    let started = Instant::now();
    let state = Rc::new(RefCell::new(State { items: sample_items(options.rows), anchor: 0, cursor: 0, sort_column: 0, ascending: true }));
    {
        let mut s = state.borrow_mut();
        sort_items(&mut s);
    }
    let model = Rc::new(VecModel::from(state.borrow().items.iter().map(to_row).collect::<Vec<_>>()));
    println!("METRIK Modellaufbau {} Zeilen: {} ms", options.rows, started.elapsed().as_millis());
    window.set_rows(ModelRc::from(model.clone()));

    let snapshots: Vec<SnapshotRow> = (0..200)
        .map(|i| SnapshotRow {
            id: format!("{:08x}", 0x9e37_79b9u32.wrapping_mul(i + 1)).into(),
            host: ["arbeits-pc", "nas", "laptop"][i as usize % 3].into(),
            paths: ["D:\\Dokumente", "/home/anna", "C:\\Users\\Anna"][i as usize % 3].into(),
            time: format!("{:02}.11.2026 {:02}:{:02}", 28 - (i % 28), 6 + i % 12, (i * 7) % 60).into(),
            tags: if i % 4 == 0 { "wichtig".into() } else { SharedString::new() },
        })
        .collect();
    window.set_snapshots(ModelRc::new(VecModel::from(snapshots)));
    window.set_hosts(ModelRc::new(VecModel::from(vec!["Alle Hosts".into(), "arbeits-pc".into(), "laptop".into(), "nas".into()])));
    window.set_status("200 Snapshot(s) geladen".into());
    {
        let s = state.borrow();
        refresh_summary(&window, &s);
    }

    window.on_row_clicked({
        let (state, model, weak) = (state.clone(), model.clone(), window.as_weak());
        move |index, ctrl, shift| {
            let started = Instant::now();
            let mut s = state.borrow_mut();
            let index = index as usize;
            if shift {
                let anchor = s.anchor;
                set_range(&mut s, &model, anchor, index, ctrl);
            } else if ctrl {
                s.items[index].selected = !s.items[index].selected;
                model.set_row_data(index, to_row(&s.items[index]));
                s.anchor = index;
            } else {
                set_range(&mut s, &model, index, index, false);
                s.anchor = index;
            }
            s.cursor = index;
            if let Some(window) = weak.upgrade() {
                refresh_summary(&window, &s);
            }
            if started.elapsed() > Duration::from_millis(30) {
                println!("METRIK Auswahl: {} ms", started.elapsed().as_millis());
            }
        }
    });
    window.on_select_all({
        let (state, model, weak) = (state.clone(), model.clone(), window.as_weak());
        move || {
            let started = Instant::now();
            let mut s = state.borrow_mut();
            let last = s.items.len().saturating_sub(1);
            set_range(&mut s, &model, 0, last, false);
            if let Some(window) = weak.upgrade() {
                refresh_summary(&window, &s);
            }
            println!("METRIK Alles auswählen ({} Zeilen): {} ms", s.items.len(), started.elapsed().as_millis());
        }
    });
    window.on_move_selection({
        let (state, model, weak) = (state.clone(), model.clone(), window.as_weak());
        move |delta, extend| {
            let mut s = state.borrow_mut();
            let target = (s.cursor as i64 + i64::from(delta)).clamp(0, s.items.len() as i64 - 1) as usize;
            if extend {
                let anchor = s.anchor;
                set_range(&mut s, &model, anchor, target, false);
            } else {
                set_range(&mut s, &model, target, target, false);
                s.anchor = target;
            }
            s.cursor = target;
            if let Some(window) = weak.upgrade() {
                refresh_summary(&window, &s);
            }
        }
    });
    window.on_header_clicked({
        let (state, model, weak) = (state.clone(), model.clone(), window.as_weak());
        move |column| {
            let started = Instant::now();
            let mut s = state.borrow_mut();
            s.ascending = if s.sort_column == column { !s.ascending } else { true };
            s.sort_column = column;
            sort_items(&mut s);
            model.set_vec(s.items.iter().map(to_row).collect::<Vec<_>>());
            if let Some(window) = weak.upgrade() {
                window.set_sort_column(column);
                window.set_sort_ascending(s.ascending);
                refresh_summary(&window, &s);
            }
            println!("METRIK Sortieren ({} Zeilen): {} ms", s.items.len(), started.elapsed().as_millis());
        }
    });
    window.on_toggle_theme({
        let weak = window.as_weak();
        move || {
            if let Some(window) = weak.upgrade() {
                let dark = !window.get_is_dark();
                apply_theme(&window, dark);
            }
        }
    });
    window.on_restore_confirmed(|target| println!("Wiederherstellen nach: {target}"));

    window.show()?;
    apply_theme(&window, options.dark);
    {
        // Zusätzlich nach dem Start: manche Plattformen setzen das Thema erst, wenn das Fenster existiert.
        let (weak, dark) = (window.as_weak(), options.dark);
        slint::Timer::single_shot(Duration::from_millis(300), move || {
            if let Some(window) = weak.upgrade() {
                apply_theme(&window, dark);
            }
        });
    }
    window.set_loading(options.loading);

    // Vorgaben für den Screenshot-Modus anwenden.
    if let Some(column) = options.sort {
        window.invoke_header_clicked(column);
    }
    if let Some((from, to)) = options.select {
        let mut s = state.borrow_mut();
        let to = to.min(s.items.len().saturating_sub(1));
        set_range(&mut s, &model, from, to, false);
        refresh_summary(&window, &s);
    }

    if options.bench {
        // Löst die Rückrufe wie die Oberfläche aus und misst Modell- und Auswahlarbeit.
        let weak = window.as_weak();
        slint::Timer::single_shot(Duration::from_millis(500), move || {
            let Some(window) = weak.upgrade() else { return };
            window.invoke_row_clicked(10, false, false);
            window.invoke_row_clicked(5_000, false, true); // Bereich 10..5000
            window.invoke_select_all();
            window.invoke_header_clicked(2); // Größe aufsteigend
            window.invoke_header_clicked(2); // Größe absteigend
            window.invoke_header_clicked(0); // Name
            window.invoke_row_clicked(0, false, false);
            window.invoke_row_clicked(99_999, false, true); // fast alles per Shift
            // Spaltenanfasser ziehen: am rechten Rand der Spalte "Name" (unsichtbar, aber bedienbar).
            let before = window.get_name_column_width();
            let (x, y) = (661.0, 158.0);
            let events = window.window();
            let at = |x: f32| slint::LogicalPosition::new(x, y);
            events.dispatch_event(slint::platform::WindowEvent::PointerMoved { position: at(x) });
            events.dispatch_event(slint::platform::WindowEvent::PointerPressed { position: at(x), button: slint::platform::PointerEventButton::Left });
            events.dispatch_event(slint::platform::WindowEvent::PointerMoved { position: at(x + 60.0) });
            events.dispatch_event(slint::platform::WindowEvent::PointerReleased { position: at(x + 60.0), button: slint::platform::PointerEventButton::Left });
            println!("TEST Spaltenbreite Name: {} -> {} (erwartet +60)", before, window.get_name_column_width());
            let _ = slint::quit_event_loop();
        });
    }
    if let Some(seconds) = options.hold {
        slint::Timer::single_shot(Duration::from_secs(seconds), || {
            let _ = slint::quit_event_loop();
        });
    }

    if let Some(path) = options.screenshot.clone() {
        let weak = window.as_weak();
        let (hover, click, popup) = (options.hover, options.click, options.popup);
        slint::Timer::single_shot(Duration::from_millis(900), move || {
            let Some(window) = weak.upgrade() else { return };
            if popup {
                window.invoke_open_dialog();
            }
            if let Some((x, y)) = click {
                // Echter Klick: gibt dem Dateibereich den Fokus und wählt die Zeile.
                let position = slint::LogicalPosition::new(x, y);
                let events = window.window();
                events.dispatch_event(slint::platform::WindowEvent::PointerMoved { position });
                events.dispatch_event(slint::platform::WindowEvent::PointerPressed { position, button: slint::platform::PointerEventButton::Left });
                events.dispatch_event(slint::platform::WindowEvent::PointerReleased { position, button: slint::platform::PointerEventButton::Left });
            }
            if let Some((x, y)) = hover {
                window.window().dispatch_event(slint::platform::WindowEvent::PointerMoved { position: slint::LogicalPosition::new(x, y) });
            }
            let path = path.clone();
            let weak = window.as_weak();
            slint::Timer::single_shot(Duration::from_millis(600), move || {
                if let Some(window) = weak.upgrade() {
                    save_png(&window, &path);
                }
                let _ = slint::quit_event_loop();
            });
        });
    }
    // Optionales zweites Fenster (echter Dialog, kein Popup) mit demselben Thema.
    let dialog = options.dialog.then(|| {
        let dialog = InfoDialog::new().expect("Dialog");
        dialog.set_is_dark(options.dark);
        dialog.show().expect("Dialog anzeigen");
        let (weak, dark) = (dialog.as_weak(), options.dark);
        slint::Timer::single_shot(Duration::from_millis(300), move || {
            if let Some(dialog) = weak.upgrade() {
                dialog.window().with_winit_window(|w| {
                    w.set_theme(Some(if dark { winit::window::Theme::Dark } else { winit::window::Theme::Light }));
                });
            }
        });
        dialog
    });
    let result = window.run();
    drop(dialog);
    result
}
