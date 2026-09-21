import * as echarts from "echarts";
import { IInputs, IOutputs } from "./generated/ManifestTypes";

type DataSet = ComponentFramework.PropertyTypes.DataSet;

// tavu_scope option values (publisher 576600xxx range)
const SCOPE = { USER: 576600000, TEAM: 576600001, LINE: 576600002, COMPANY: 576600003 };
type ScopeKey = "COMPANY" | "LINE" | "USER";

// Column logical names on tavu_reportingcache (the bound view must include these).
const COL = {
    scope: "tavu_scope",
    name: "tavu_name",
    target: "tavu_targetamount",
    won: "tavu_closedwonamount",
    committed: "tavu_committedamount",
    bestcase: "tavu_bestcaseamount",
    open: "tavu_openpipelineamount",
    forecast: "tavu_forecastamount",
    attainment: "tavu_attainment",
    gap: "tavu_gaptogoal",
    coverage: "tavu_coverageratio",
    period: "tavu_salesperiod",
    rep: "tavu_salesrep",
    line: "tavu_businessline"
};

interface Row {
    scope: number;
    label: string;      // subject label (rep / line / company name)
    period: string;
    target: number;
    won: number;
    committed: number;
    bestcase: number;
    open: number;
    forecast: number;
    attainment: number | null;
    gap: number;
    coverage: number | null;
}

export class ForecastDashboard implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private root!: HTMLDivElement;
    private tilesEl!: HTMLDivElement;
    private scopeBar!: HTMLDivElement;
    private titleEl!: HTMLSpanElement;
    private compChart!: echarts.ECharts;
    private attChart!: echarts.ECharts;
    private compEl!: HTMLDivElement;
    private attEl!: HTMLDivElement;
    private scope: ScopeKey = "COMPANY";
    private rows: Row[] = [];
    private ro?: ResizeObserver;
    private container!: HTMLDivElement;
    private resizePending = false;
    private onWinResize = (): void => this.resizeCharts();
    private s!: Record<string, string>;

    public init(
        context: ComponentFramework.Context<IInputs>,
        _notifyOutputChanged: () => void,
        _state: ComponentFramework.Dictionary,
        container: HTMLDivElement
    ): void {
        context.mode.trackContainerResize(true);
        this.container = container;

        // Localized UI strings (resx: 1033 English, 3082 Spanish). Read once here from the
        // host's resource set so the control follows the user's Dataverse language.
        const g = (k: string): string => context.resources.getString(k);
        this.s = {
            title: g("title"),
            scopeCompany: g("scopeCompany"),
            scopeLine: g("scopeLine"),
            scopeRep: g("scopeRep"),
            cardComposition: g("cardComposition"),
            cardAttainment: g("cardAttainment"),
            tileAttainment: g("tileAttainment"),
            tileForecast: g("tileForecast"),
            tileGap: g("tileGap"),
            tileCoverage: g("tileCoverage"),
            ofAmount: g("ofAmount"),
            pctOfTarget: g("pctOfTarget"),
            goalMet: g("goalMet"),
            openAmount: g("openAmount"),
            seriesWon: g("seriesWon"),
            seriesCommitted: g("seriesCommitted"),
            seriesBestCase: g("seriesBestCase"),
            seriesOpen: g("seriesOpen"),
            seriesTarget: g("seriesTarget"),
            emptyState: g("emptyState"),
            notAvailable: g("notAvailable")
        };

        this.root = document.createElement("div");
        this.root.className = "otf-root";

        // Header: title + scope switch
        const header = document.createElement("div");
        header.className = "otf-header";
        this.titleEl = document.createElement("span");
        this.titleEl.className = "otf-title";
        this.titleEl.textContent = this.s.title;
        this.scopeBar = document.createElement("div");
        this.scopeBar.className = "otf-scopes";
        (
            [
                ["COMPANY", this.s.scopeCompany],
                ["LINE", this.s.scopeLine],
                ["USER", this.s.scopeRep]
            ] as [ScopeKey, string][]
        ).forEach(([key, text]) => {
            const b = document.createElement("button");
            b.className = "otf-scope-btn";
            b.type = "button";
            b.textContent = text;
            b.setAttribute("data-scope", key);
            b.setAttribute("aria-pressed", key === this.scope ? "true" : "false");
            b.addEventListener("click", () => {
                this.scope = key;
                this.syncScopeButtons();
                this.render();
            });
            this.scopeBar.appendChild(b);
        });
        header.appendChild(this.titleEl);
        header.appendChild(this.scopeBar);
        this.root.appendChild(header);

        // KPI tiles
        this.tilesEl = document.createElement("div");
        this.tilesEl.className = "otf-tiles";
        this.root.appendChild(this.tilesEl);

        // Charts
        const charts = document.createElement("div");
        charts.className = "otf-charts";
        this.compEl = this.card(charts, this.s.cardComposition);
        this.attEl = this.card(charts, this.s.cardAttainment);
        this.root.appendChild(charts);

        container.appendChild(this.root);

        this.compChart = echarts.init(this.compEl, undefined, { renderer: "svg" });
        this.attChart = echarts.init(this.attEl, undefined, { renderer: "svg" });

        this.ro = new ResizeObserver(() => this.resizeCharts());
        this.ro.observe(this.container);
        window.addEventListener("resize", this.onWinResize);
    }

    public updateView(context: ComponentFramework.Context<IInputs>): void {
        this.sizeToHost(context);
        const ds = context.parameters.cacheDataSet as DataSet;
        if (!ds || ds.loading) return;
        this.rows = this.readRows(ds);
        // Title carries the period when the rows agree on one.
        const periods = Array.from(new Set(this.rows.map(r => r.period).filter(Boolean)));
        this.titleEl.innerHTML =
            escapeHtml(this.s.title) + (periods.length === 1 ? ` <span class="otf-period">· ${escapeHtml(periods[0])}</span>` : "");
        this.render();
    }

    public getOutputs(): IOutputs {
        return {};
    }

    public destroy(): void {
        this.ro?.disconnect();
        window.removeEventListener("resize", this.onWinResize);
        this.compChart?.dispose();
        this.attChart?.dispose();
    }

    /**
     * Keep the control FLUID (100% of the host container). Do NOT pin a fixed px width from
     * allocatedWidth: that value only refreshes on updateView, so a live window drag would
     * leave the control wider than its container (horizontal scrollbar, clipped legend) until
     * the next refresh. At 100% the control shrinks with the container and the window-resize /
     * ResizeObserver handlers re-fit the charts to the new width immediately.
     */
    private sizeToHost(_context: ComponentFramework.Context<IInputs>): void {
        this.root.style.width = "100%";
        this.resizeCharts();
    }

    // ---- data ----------------------------------------------------------------

    private readRows(ds: DataSet): Row[] {
        const out: Row[] = [];
        for (const id of ds.sortedRecordIds) {
            const rec = ds.records[id];
            if (!rec) continue;
            const scope = num(rec.getValue(COL.scope));
            const label =
                str(rec.getFormattedValue(COL.rep)) ||
                str(rec.getFormattedValue(COL.line)) ||
                str(rec.getValue(COL.name)) ||
                "(unnamed)";
            out.push({
                scope,
                label,
                period: str(rec.getFormattedValue(COL.period)),
                target: num(rec.getValue(COL.target)),
                won: num(rec.getValue(COL.won)),
                committed: num(rec.getValue(COL.committed)),
                bestcase: num(rec.getValue(COL.bestcase)),
                open: num(rec.getValue(COL.open)),
                forecast: num(rec.getValue(COL.forecast)),
                attainment: numOrNull(rec.getValue(COL.attainment)),
                gap: num(rec.getValue(COL.gap)),
                coverage: numOrNull(rec.getValue(COL.coverage))
            });
        }
        return out;
    }

    private rowsForScope(): Row[] {
        const want = SCOPE[this.scope];
        return this.rows.filter(r => r.scope === want);
    }

    // ---- render --------------------------------------------------------------

    private render(): void {
        this.applyHostTheme();
        const rows = this.rowsForScope();
        if (rows.length === 0) {
            this.tilesEl.innerHTML = "";
            this.setEmpty(true);
            this.compChart.clear();
            this.attChart.clear();
            return;
        }
        this.setEmpty(false);
        this.renderTiles(rows);
        this.renderComposition(rows);
        this.renderAttainment(rows);
        this.resizeCharts();
    }

    private renderTiles(rows: Row[]): void {
        // Aggregate across the scope's rows (one row for Company; recompute % from bases).
        const t = sum(rows, r => r.target);
        const won = sum(rows, r => r.won);
        const committed = sum(rows, r => r.committed);
        const open = sum(rows, r => r.open);
        const forecast = won + committed;
        const gap = t - won;
        const attain = t > 0 ? (won / t) * 100 : null;
        const coverage = gap > 0 ? open / gap : null;

        const na = this.s.notAvailable;
        const tiles: { label: string; value: string; sub?: string; good?: boolean }[] = [
            { label: this.s.tileAttainment, value: pct(attain, na), sub: fmt(this.s.ofAmount, money(won), money(t)), good: (attain ?? 0) >= 100 },
            { label: this.s.tileForecast, value: money(forecast), sub: fmt(this.s.pctOfTarget, pct(t > 0 ? (forecast / t) * 100 : null, na)) },
            { label: this.s.tileGap, value: money(gap) },
            { label: this.s.tileCoverage, value: coverage == null ? this.s.goalMet : `${coverage.toFixed(2)}x`, sub: fmt(this.s.openAmount, money(open)) }
        ];
        this.tilesEl.innerHTML = tiles
            .map(
                x => `<div class="otf-tile"><div class="otf-label">${escapeHtml(x.label)}</div>` +
                    `<div class="otf-value${x.good ? " good" : ""}">${escapeHtml(x.value)}</div>` +
                    (x.sub ? `<div class="otf-sub">${escapeHtml(x.sub)}</div>` : "") +
                    `</div>`
            )
            .join("");
    }

    private renderComposition(rows: Row[]): void {
        const c = this.css();
        const cats = rows.map(r => r.label);
        const seg = (pick: (r: Row) => number) => rows.map(pick);
        const targetData = rows.map((r, i) => [r.target, i]);

        const option: any = {
                textStyle: { fontFamily: "system-ui, -apple-system, Segoe UI, sans-serif", color: c.ink2 },
                grid: { left: 8, right: 24, top: 30, bottom: 8, containLabel: true },
                legend: {
                    type: "scroll",
                    top: 0,
                    left: "center",
                    textStyle: { color: c.ink2 },
                    pageIconColor: c.ink2,
                    pageTextStyle: { color: c.muted },
                    data: [this.s.seriesWon, this.s.seriesCommitted, this.s.seriesBestCase, this.s.seriesOpen, this.s.seriesTarget]
                },
                tooltip: {
                    trigger: "axis",
                    axisPointer: { type: "shadow" },
                    valueFormatter: (v: number) => money(v)
                },
                xAxis: {
                    type: "value",
                    axisLabel: { color: c.muted, formatter: (v: number) => moneyShort(v) },
                    splitLine: { lineStyle: { color: c.grid } },
                    axisLine: { lineStyle: { color: c.baseline } }
                },
                yAxis: {
                    type: "category",
                    data: cats,
                    axisLabel: { color: c.ink2 },
                    axisLine: { lineStyle: { color: c.baseline } },
                    axisTick: { show: false }
                },
                series: [
                    this.stackSeries(this.s.seriesWon, seg(r => r.won), c.won),
                    this.stackSeries(this.s.seriesCommitted, seg(r => r.committed), c.committed),
                    this.stackSeries(this.s.seriesBestCase, seg(r => r.bestcase), c.bestcase),
                    this.stackSeries(this.s.seriesOpen, seg(r => r.open), c.open),
                    {
                        name: this.s.seriesTarget,
                        type: "scatter",
                        symbol: "diamond",
                        symbolSize: 14,
                        data: targetData,
                        itemStyle: { color: "transparent", borderColor: c.target, borderWidth: 2 },
                        tooltip: { show: true },
                        z: 5
                    }
                ]
        };
        this.compChart.setOption(option, true);
    }

    private stackSeries(name: string, data: number[], color: string): any {
        return {
            name,
            type: "bar",
            stack: "total",
            barWidth: rows_barWidth(data.length),
            itemStyle: { color, borderColor: this.css().surface, borderWidth: 2, borderRadius: 2 },
            emphasis: { focus: "series" },
            data
        };
    }

    private renderAttainment(rows: Row[]): void {
        const c = this.css();
        const cats = rows.map(r => r.label);
        const data = rows.map(r => {
            const v = r.target > 0 ? (r.won / r.target) * 100 : 0;
            return { value: +v.toFixed(1), itemStyle: { color: v >= 100 ? c.good : c.committed, borderRadius: 2 } };
        });
        const option: any = {
                textStyle: { fontFamily: "system-ui, -apple-system, Segoe UI, sans-serif", color: c.ink2 },
                grid: { left: 8, right: 40, top: 28, bottom: 8, containLabel: true },
                tooltip: { trigger: "axis", axisPointer: { type: "shadow" }, valueFormatter: (v: number) => `${v}%` },
                xAxis: {
                    type: "value",
                    max: (val: { max: number }) => Math.max(100, Math.ceil(val.max / 10) * 10),
                    axisLabel: { color: c.muted, formatter: "{value}%" },
                    splitLine: { lineStyle: { color: c.grid } },
                    axisLine: { lineStyle: { color: c.baseline } }
                },
                yAxis: {
                    type: "category",
                    data: cats,
                    axisLabel: { color: c.ink2 },
                    axisLine: { lineStyle: { color: c.baseline } },
                    axisTick: { show: false }
                },
                series: [
                    {
                        type: "bar",
                        barWidth: rows_barWidth(data.length),
                        data,
                        label: { show: true, position: "right", color: c.ink2, formatter: (p: { value: number }) => `${p.value}%` },
                        markLine: {
                            symbol: "none",
                            silent: true,
                            lineStyle: { color: c.target, type: "dashed", width: 1 },
                            data: [{ xAxis: 100 }],
                            label: { show: true, formatter: "100%", color: c.muted, position: "end" }
                        }
                    }
                ]
        };
        this.attChart.setOption(option, true);
    }

    // ---- helpers -------------------------------------------------------------

    private card(parent: HTMLElement, title: string): HTMLDivElement {
        const card = document.createElement("div");
        card.className = "otf-card";
        const h = document.createElement("h3");
        h.textContent = title;
        const chart = document.createElement("div");
        chart.className = "otf-chart";
        card.appendChild(h);
        card.appendChild(chart);
        parent.appendChild(card);
        return chart;
    }

    private syncScopeButtons(): void {
        this.scopeBar.querySelectorAll<HTMLButtonElement>(".otf-scope-btn").forEach(b => {
            b.setAttribute("aria-pressed", b.getAttribute("data-scope") === this.scope ? "true" : "false");
        });
    }

    private setEmpty(empty: boolean): void {
        (this.compEl.parentElement as HTMLElement).style.display = empty ? "none" : "";
        (this.attEl.parentElement as HTMLElement).style.display = empty ? "none" : "";
        if (empty && !this.root.querySelector(".otf-empty")) {
            const e = document.createElement("div");
            e.className = "otf-empty";
            e.textContent = this.s.emptyState;
            this.tilesEl.after(e);
        } else if (!empty) {
            this.root.querySelector(".otf-empty")?.remove();
        }
    }

    private resizeCharts(): void {
        // Coalesce bursts of resize events to one frame, then re-fit; a second pass after the
        // host layout settles catches the grow-then-shrink case where the container width
        // lags the event by a frame or two.
        if (this.resizePending) return;
        this.resizePending = true;
        requestAnimationFrame(() => {
            this.resizePending = false;
            this.doResize();
            setTimeout(() => this.doResize(), 160);
        });
    }

    /**
     * Re-fit each chart to the EXACT pixel size of its container, handling shrink as well as grow.
     *
     * The grow-then-shrink trap: ECharts renders an SVG at the current (large) width inside the
     * chart div. When the window then shrinks, that rendered content can hold the container wide
     * (intrinsic min-content), so reading dom.clientWidth returns the STALE large width and the
     * chart never shrinks. To read the TRUE available width we first collapse the chart div to 0,
     * force a synchronous reflow, and only then restore it to 100% and measure, by which point
     * the container has reflowed to the real (possibly smaller) space. Passing explicit
     * width/height to resize() forces ECharts to adopt it in both directions.
     */
    private doResize(): void {
        [this.compChart, this.attChart].forEach(ch => {
            if (!ch) return;
            const dom = ch.getDom() as HTMLElement;
            dom.style.width = "0px";
            void dom.offsetWidth;      // flush layout so the parent can reflow to real width
            dom.style.width = "100%";
            const w = dom.clientWidth;
            const h = dom.clientHeight;
            if (w > 0 && h > 0) ch.resize({ width: w, height: h });
        });
    }

    /**
     * Follow the HOST app's theme, not the OS. Walk up from the control's container to the
     * first ancestor with an opaque background, read its luminance, and set data-mode
     * accordingly. This keeps the control's surface matching the model-driven app whether
     * it is light or dark, regardless of the operating-system color-scheme.
     */
    private applyHostTheme(): void {
        let el: HTMLElement | null = this.root.parentElement;
        let bg = "";
        for (let hops = 0; el && hops < 12; hops++, el = el.parentElement) {
            const c = getComputedStyle(el).backgroundColor;
            if (c && c !== "transparent" && !/rgba?\(0,\s*0,\s*0,\s*0\)/.test(c)) {
                bg = c;
                break;
            }
        }
        if (!bg) bg = getComputedStyle(document.body).backgroundColor || "rgb(255,255,255)";
        const m = bg.match(/[\d.]+/g);
        let dark = false;
        if (m && m.length >= 3) {
            const r = +m[0], g = +m[1], b = +m[2];
            const lum = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255;
            dark = lum < 0.5;
        }
        this.root.setAttribute("data-mode", dark ? "dark" : "light");
    }

    private css() {
        const s = getComputedStyle(this.root);
        const v = (n: string) => s.getPropertyValue(n).trim();
        return {
            surface: v("--surface-1"),
            ink: v("--ink"),
            ink2: v("--ink-2"),
            muted: v("--muted"),
            grid: v("--grid"),
            baseline: v("--baseline"),
            won: v("--won"),
            committed: v("--committed"),
            bestcase: v("--bestcase"),
            open: v("--open"),
            good: v("--good"),
            target: v("--target")
        };
    }
}

// ---- module helpers ---------------------------------------------------------

function rows_barWidth(n: number): string {
    return n <= 1 ? "38%" : n <= 3 ? "52%" : "64%";
}
function num(v: unknown): number {
    const n = typeof v === "number" ? v : parseFloat(String(v ?? ""));
    return isNaN(n) ? 0 : n;
}
function numOrNull(v: unknown): number | null {
    if (v === null || v === undefined || v === "") return null;
    const n = typeof v === "number" ? v : parseFloat(String(v));
    return isNaN(n) ? null : n;
}
function str(v: unknown): string {
    return v === null || v === undefined ? "" : String(v);
}
function sum(rows: Row[], pick: (r: Row) => number): number {
    return rows.reduce((a, r) => a + pick(r), 0);
}
function pct(v: number | null, na: string): string {
    return v == null ? na : `${v.toFixed(0)}%`;
}
function fmt(template: string, ...args: string[]): string {
    return template.replace(/\{(\d+)\}/g, (_m, i) => args[+i] ?? "");
}
function money(v: number): string {
    return new Intl.NumberFormat(undefined, { style: "currency", currency: "USD", maximumFractionDigits: 0 }).format(v || 0);
}
function moneyShort(v: number): string {
    if (Math.abs(v) >= 1000000) return `$${(v / 1000000).toFixed(1)}M`;
    if (Math.abs(v) >= 1000) return `$${(v / 1000).toFixed(0)}k`;
    return `$${v}`;
}
function escapeHtml(s: string): string {
    return s.replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c] as string));
}
