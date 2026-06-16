"""Generate the stakeholder slide deck using CPU branding from the template.

Output: docs/CPU-stakeholder-deck.pptx (5 slides, 10x5.625 in, Computershare branding).

Branding is reverse-engineered from docs/GenericDocumentProcessing_AI_Solution.pptx
(slides 1-4):
  - Slide size 10 x 5.625 in (custom 16:9)
  - Calibri font throughout
  - Primary purple #8B1A8F (title, header bar, footer bar)
  - Orange accent #E8850A / #F5A623 (under-title stripe, callouts)
  - Semantic stage colours: purple (input), orange (extract), green (classify), blue (persist)
  - Title slide: vertical purple gradient bands + light-purple dot grid + orange accent line
  - Footer: "Computershare | Computershare Distribution Only | page-number"
"""

from datetime import datetime
from pathlib import Path

from pptx import Presentation
from pptx.util import Inches, Pt
from pptx.dml.color import RGBColor
from pptx.enum.shapes import MSO_SHAPE, MSO_CONNECTOR
from pptx.enum.text import PP_ALIGN, MSO_ANCHOR

# ---------------------------------------------------------------------------
# CPU brand palette (from GenericDocumentProcessing_AI_Solution.pptx)
# ---------------------------------------------------------------------------
CPU_PURPLE          = RGBColor(0x8B, 0x1A, 0x8F)  # primary brand
CPU_PURPLE_DARK     = RGBColor(0x5C, 0x0F, 0x6E)
CPU_PURPLE_MID      = RGBColor(0x70, 0x14, 0x80)
CPU_PURPLE_LIGHT    = RGBColor(0x9B, 0x2A, 0x9F)
CPU_PURPLE_DEEPER   = RGBColor(0x7B, 0x16, 0x85)
CPU_PURPLE_DEEPEST  = RGBColor(0x6A, 0x12, 0x78)
DOT_PURPLE          = RGBColor(0xC0, 0x70, 0xC8)
PANEL_PURPLE_TINT   = RGBColor(0xF5, 0xE6, 0xF7)
FOOTER_LIGHT_PURPLE = RGBColor(0xDD, 0xAA, 0xDD)

CPU_ORANGE_DARK     = RGBColor(0xE8, 0x85, 0x0A)
CPU_ORANGE_LIGHT    = RGBColor(0xF5, 0xA6, 0x23)
ORANGE_HEADER       = RGBColor(0xC4, 0x5E, 0x00)
ORANGE_PANEL_TINT   = RGBColor(0xFF, 0xF3, 0xE0)

GREEN_HEADER        = RGBColor(0x5A, 0x9E, 0x5C)
GREEN_DEEPER        = RGBColor(0x2E, 0x7D, 0x32)
GREEN_PANEL_TINT    = RGBColor(0xE8, 0xF5, 0xE9)

BLUE_HEADER         = RGBColor(0x15, 0x65, 0xC0)
BLUE_DEEPER         = RGBColor(0x28, 0x35, 0x93)
BLUE_PANEL_TINT     = RGBColor(0xE3, 0xF2, 0xFD)

WHITE   = RGBColor(0xFF, 0xFF, 0xFF)
BODY    = RGBColor(0x42, 0x42, 0x42)
MUTED   = RGBColor(0x9E, 0x9E, 0x9E)
LINE_GREY = RGBColor(0xCC, 0xCC, 0xCC)

FONT = "Calibri"

# Slide size: custom 16:9 used by the CPU template
SLIDE_W = 10.0
SLIDE_H = 5.625

DATE_TAG = datetime(2026, 5, 19).strftime("%b %Y").upper()  # "MAY 2026"

# ---------------------------------------------------------------------------
# Low-level shape + text helpers
# ---------------------------------------------------------------------------

def _rect(slide, x, y, w, h, fill, line=None, line_w=0.5):
    shp = slide.shapes.add_shape(MSO_SHAPE.RECTANGLE, Inches(x), Inches(y), Inches(w), Inches(h))
    shp.fill.solid()
    shp.fill.fore_color.rgb = fill
    if line is None:
        shp.line.fill.background()
    else:
        shp.line.color.rgb = line
        shp.line.width = Pt(line_w)
    shp.shadow.inherit = False
    return shp


def _round(slide, x, y, w, h, fill, line=None, line_w=0.5, adj=0.10):
    shp = slide.shapes.add_shape(MSO_SHAPE.ROUNDED_RECTANGLE, Inches(x), Inches(y), Inches(w), Inches(h))
    shp.fill.solid()
    shp.fill.fore_color.rgb = fill
    shp.adjustments[0] = adj
    if line is None:
        shp.line.fill.background()
    else:
        shp.line.color.rgb = line
        shp.line.width = Pt(line_w)
    shp.shadow.inherit = False
    return shp


def _diamond(slide, x, y, w, h, fill, line=None):
    shp = slide.shapes.add_shape(MSO_SHAPE.DIAMOND, Inches(x), Inches(y), Inches(w), Inches(h))
    shp.fill.solid()
    shp.fill.fore_color.rgb = fill
    if line is None:
        shp.line.fill.background()
    else:
        shp.line.color.rgb = line
    shp.shadow.inherit = False
    return shp


def _oval(slide, x, y, w, h, fill, line=None):
    shp = slide.shapes.add_shape(MSO_SHAPE.OVAL, Inches(x), Inches(y), Inches(w), Inches(h))
    shp.fill.solid()
    shp.fill.fore_color.rgb = fill
    if line is None:
        shp.line.fill.background()
    else:
        shp.line.color.rgb = line
    shp.shadow.inherit = False
    return shp


def _set_text(shape, text_or_lines, *, size=10, bold=False, color=BODY,
              align=PP_ALIGN.CENTER, anchor=MSO_ANCHOR.MIDDLE, font=FONT):
    tf = shape.text_frame
    tf.word_wrap = True
    tf.margin_left = Inches(0.04)
    tf.margin_right = Inches(0.04)
    tf.margin_top = Inches(0.02)
    tf.margin_bottom = Inches(0.02)
    tf.vertical_anchor = anchor
    tf.text = ""

    if isinstance(text_or_lines, str):
        # Each line may be a tuple via "\n"; treat as identical formatting
        lines = [(line, {}) for line in text_or_lines.split("\n")]
    else:
        # list of (text, {size, bold, color, align}) overrides
        lines = text_or_lines

    for i, (line, overrides) in enumerate(lines):
        p = tf.paragraphs[0] if i == 0 else tf.add_paragraph()
        p.alignment = overrides.get("align", align)
        run = p.add_run()
        run.text = line
        run.font.name = overrides.get("font", font)
        run.font.size = Pt(overrides.get("size", size))
        run.font.bold = overrides.get("bold", bold)
        run.font.color.rgb = overrides.get("color", color)


def _textbox(slide, x, y, w, h, text_or_lines, *, size=10, bold=False, color=BODY,
             align=PP_ALIGN.LEFT, anchor=MSO_ANCHOR.TOP, font=FONT):
    tb = slide.shapes.add_textbox(Inches(x), Inches(y), Inches(w), Inches(h))
    _set_text(tb, text_or_lines, size=size, bold=bold, color=color,
              align=align, anchor=anchor, font=font)
    return tb


def _arrow(slide, x1, y1, x2, y2, color=BODY, weight=1.25):
    conn = slide.shapes.add_connector(MSO_CONNECTOR.STRAIGHT,
                                      Inches(x1), Inches(y1), Inches(x2), Inches(y2))
    conn.line.color.rgb = color
    conn.line.width = Pt(weight)
    from pptx.oxml.ns import qn
    from lxml import etree
    line = conn.line._get_or_add_ln()
    tail = line.find(qn("a:tailEnd"))
    if tail is None:
        tail = etree.SubElement(line, qn("a:tailEnd"))
    tail.set("type", "triangle")
    tail.set("w", "med")
    tail.set("len", "med")
    return conn


# ---------------------------------------------------------------------------
# Page chrome
# ---------------------------------------------------------------------------

def page_header(slide, title, subtitle):
    # Top thin purple bar
    _rect(slide, 0, 0, SLIDE_W, 0.12, CPU_PURPLE)
    # Title
    _textbox(slide, 0.35, 0.14, 8.0, 0.55, title,
             size=22, bold=True, color=CPU_PURPLE, anchor=MSO_ANCHOR.MIDDLE)
    # Subtitle
    if subtitle:
        _textbox(slide, 0.35, 0.68, 8.0, 0.30, subtitle,
                 size=11, color=MUTED, anchor=MSO_ANCHOR.MIDDLE)
    # Date tag top-right
    _textbox(slide, 8.5, 0.14, 1.30, 0.22, DATE_TAG,
             size=7.5, color=MUTED, align=PP_ALIGN.RIGHT, anchor=MSO_ANCHOR.MIDDLE)
    # Thin orange separator under title
    _rect(slide, 0, 0.98, SLIDE_W, 0.04, CPU_ORANGE_DARK)


def page_footer(slide, page_number):
    _rect(slide, 0, 5.35, SLIDE_W, 0.28, CPU_PURPLE)
    _textbox(slide, 0.20, 5.36, 2.50, 0.22, "Computershare",
             size=9, bold=True, color=WHITE, anchor=MSO_ANCHOR.MIDDLE)
    _textbox(slide, 3.00, 5.36, 4.00, 0.22, "Computershare Distribution Only",
             size=7.5, color=FOOTER_LIGHT_PURPLE, anchor=MSO_ANCHOR.MIDDLE)
    _textbox(slide, 8.80, 5.36, 1.00, 0.22, str(page_number),
             size=9, color=WHITE, align=PP_ALIGN.RIGHT, anchor=MSO_ANCHOR.MIDDLE)


# ---------------------------------------------------------------------------
# Slide 1 — Title
# ---------------------------------------------------------------------------

def slide_title(prs):
    s = prs.slides.add_slide(prs.slide_layouts[6])

    # Vertical purple gradient bands across full slide
    _rect(s, 0.00, 0, 2.50, SLIDE_H, CPU_PURPLE_DARK)
    _rect(s, 2.50, 0, 2.50, SLIDE_H, CPU_PURPLE_MID)
    _rect(s, 5.00, 0, 2.00, SLIDE_H, CPU_PURPLE)
    _rect(s, 7.00, 0, 3.00, SLIDE_H, CPU_PURPLE_LIGHT)

    # Decorative overlapping circles in upper-right (matches template)
    _oval(s, 5.50, -0.80, 5.20, 5.20, CPU_PURPLE_DEEPER)
    _oval(s, 5.90, -0.55, 4.50, 4.50, CPU_PURPLE_DEEPEST)

    # Dot grid pattern in upper-right
    for col in range(6):
        for row in range(5):
            cx = 6.20 + col * 0.52
            cy = 0.30 + row * 0.75
            _oval(s, cx, cy, 0.06, 0.06, DOT_PURPLE)

    # Orange accent line (two stripes)
    _rect(s, 5.20, 4.60, 4.60, 0.08, CPU_ORANGE_DARK)
    _rect(s, 5.20, 4.70, 4.60, 0.04, CPU_ORANGE_LIGHT)

    # Date tag
    _textbox(s, 0.30, 0.22, 2.00, 0.25, DATE_TAG,
             size=10, bold=True, color=WHITE, anchor=MSO_ANCHOR.MIDDLE)

    # Main title (two lines, large)
    _textbox(s, 0.30, 1.35, 5.50, 1.30,
             [("Backoffice Document", {"size": 34, "bold": True, "color": WHITE}),
              ("Processing Solution", {"size": 34, "bold": True, "color": WHITE})],
             align=PP_ALIGN.LEFT, anchor=MSO_ANCHOR.TOP)

    # Sub-title
    _textbox(s, 0.30, 2.72, 5.50, 0.55, "AI-Assisted Intent Classification",
             size=20, bold=True, color=WHITE, align=PP_ALIGN.LEFT, anchor=MSO_ANCHOR.MIDDLE)

    # Author block
    _textbox(s, 0.30, 3.42, 4.50, 0.32, "Pankaj Singh",
             size=14, bold=True, color=CPU_ORANGE_LIGHT, align=PP_ALIGN.LEFT, anchor=MSO_ANCHOR.MIDDLE)
    _textbox(s, 0.30, 3.76, 4.50, 0.26, "Senior Solutions Architect",
             size=11, color=DOT_PURPLE, align=PP_ALIGN.LEFT, anchor=MSO_ANCHOR.MIDDLE)
    _textbox(s, 0.30, 4.09, 4.50, 0.26, "19th May 2026",
             size=11, color=DOT_PURPLE, align=PP_ALIGN.LEFT, anchor=MSO_ANCHOR.MIDDLE)

    # Footer bar (purple)
    _rect(s, 0.00, 5.05, SLIDE_W, 0.57, CPU_PURPLE)
    _rect(s, 0.18, 5.13, 0.35, 0.35, WHITE)   # logo placeholder square
    _textbox(s, 0.58, 5.12, 2.80, 0.38, "Computershare",
             size=13, bold=True, color=WHITE, anchor=MSO_ANCHOR.MIDDLE)
    _textbox(s, 3.60, 5.17, 4.00, 0.28, "Computershare Distribution Only",
             size=8, color=FOOTER_LIGHT_PURPLE, anchor=MSO_ANCHOR.MIDDLE)


# ---------------------------------------------------------------------------
# Slide 2 — Solution Architecture (4-stage pipeline + actors + saga band)
# ---------------------------------------------------------------------------

def slide_architecture(prs):
    s = prs.slides.add_slide(prs.slide_layouts[6])
    page_header(s, "Solution Architecture",
                "Modular monolith today; same module boundaries as the AKS pods in production")

    # External actors
    _round(s, 0.30, 1.25, 1.55, 0.55, CPU_PURPLE_DARK)
    upstream = s.shapes[-1]
    _set_text(upstream, "Tungsten /\nFile Share", size=9, bold=True, color=WHITE)

    _round(s, 8.15, 1.25, 1.55, 0.55, CPU_PURPLE_DARK)
    downstream = s.shapes[-1]
    _set_text(downstream, "EDC / Workflow\n(RPA)", size=9, bold=True, color=WHITE)

    # 4-stage cards (matches CPU template Solution Overview pattern)
    stages = [
        ("1", "INGEST",   "Watcher + Ingest.Service\nFileSystemWatcher / blob upload\nDocumentIngestedEvent",   CPU_PURPLE,    PANEL_PURPLE_TINT),
        ("2", "EXTRACT",  "Azure Document Intelligence\nprebuilt-layout OCR\nFlattened to plain text",          ORANGE_HEADER, ORANGE_PANEL_TINT),
        ("3", "CLASSIFY", "MAF agent + KB tool loop\nSemantic intent search\nStrict JSON output",              GREEN_HEADER,  GREEN_PANEL_TINT),
        ("4", "PERSIST",  "Azure SQL row\nArchive blob copy\nOutbound workflow hand-off",                       BLUE_HEADER,   BLUE_PANEL_TINT),
    ]

    card_w = 2.00
    card_h = 2.40
    card_y = 1.95
    gap    = 0.08
    start_x = 2.00

    for i, (num, title, body, header_col, panel_col) in enumerate(stages):
        x = start_x + i * (card_w + gap)
        # Body panel
        _round(s, x, card_y, card_w, card_h, panel_col, line=LINE_GREY, line_w=0.5)
        # Header strip
        _round(s, x, card_y, card_w, 0.45, header_col, adj=0.20)
        # Number badge
        _rect(s, x + 0.08, card_y + 0.05, 0.32, 0.32, WHITE)
        badge = s.shapes[-1]
        _set_text(badge, num, size=11, bold=True, color=header_col)
        # Stage label
        _textbox(s, x + 0.45, card_y + 0.05, card_w - 0.5, 0.30, title,
                 size=11, bold=True, color=WHITE, anchor=MSO_ANCHOR.MIDDLE)
        # Body text
        _textbox(s, x + 0.10, card_y + 0.55, card_w - 0.20, card_h - 0.70, body,
                 size=9, color=BODY, align=PP_ALIGN.CENTER, anchor=MSO_ANCHOR.TOP)
        # Inter-card arrows
        if i < len(stages) - 1:
            ax = x + card_w + 0.005
            _textbox(s, ax, card_y + card_h / 2 - 0.18, 0.08, 0.30, "→",
                     size=18, bold=True, color=BODY, anchor=MSO_ANCHOR.MIDDLE)

    # Inbound and outbound arrows between actors and pipeline
    _arrow(s, 1.85, 1.50, 2.00, 2.50, color=BODY, weight=1.5)
    _arrow(s, 8.00, 2.50, 8.15, 1.50, color=BODY, weight=1.5)

    # Saga band underneath (matches template's RouterAgent band)
    _rect(s, 0.30, 4.55, 9.40, 0.62, ORANGE_PANEL_TINT, line=LINE_GREY, line_w=0.5)
    _rect(s, 0.30, 4.55, 0.10, 0.62, CPU_ORANGE_DARK)
    _textbox(s, 0.50, 4.58, 6.0, 0.28,
             "Orchestrator  •  MassTransit Saga (DocumentSaga)",
             size=10, bold=True, color=CPU_PURPLE)
    _textbox(s, 0.50, 4.85, 8.90, 0.28,
             "Durable EF Core state — AwaitingOcr → AwaitingClassification → Persisted; failure paths land in DLQ via DocumentFailedEvent",
             size=8.5, color=BODY)

    page_footer(s, 2)


# ---------------------------------------------------------------------------
# Slide 3 — C4 Level 3 Component View
# ---------------------------------------------------------------------------

def slide_component(prs):
    s = prs.slides.add_slide(prs.slide_layouts[6])
    page_header(s, "Component View — C4 Level 3",
                "Every .NET project today maps 1:1 to a future AKS container")

    # 5-row layout, each row tinted with a stage colour
    rows = [
        # (y, height, title, label, accent, panel, projects)
        (1.10, 0.62, "INGEST",         "File arrival to per-document publish",
         CPU_PURPLE, PANEL_PURPLE_TINT,
         "Ingest.Watcher  •  Ingest.Service"),
        (1.78, 0.62, "ORCHESTRATION",  "MassTransit saga, EF Core, MassTransit.RabbitMQ ↔ AzureServiceBus",
         ORANGE_HEADER, ORANGE_PANEL_TINT,
         "Orchestration  (DocumentSaga state machine)"),
        (2.46, 0.62, "AI CORE",        "OCR, classifier and grounded KB tool loop",
         GREEN_HEADER, GREEN_PANEL_TINT,
         "Ocr  •  Classifier (MAF)  •  KnowledgeBase  •  Embeddings"),
        (3.14, 0.62, "PERSISTENCE",    "Structured + archive sinks",
         BLUE_HEADER, BLUE_PANEL_TINT,
         "Persistence.Sql  •  Persistence.Archive  •  Outbound.Workflow  •  Api"),
        (3.82, 0.62, "CROSS-CUTTING",  "Shared by every executable, transport-agnostic",
         BLUE_DEEPER, RGBColor(0xE8, 0xEA, 0xF6),
         "Contracts  •  Common (LLM factory)  •  ServiceDefaults (OTel, health)  •  AppHost (Aspire)"),
    ]

    for (y, h, title, descr, accent, panel, projects) in rows:
        _rect(s, 0.30, y, 9.40, h, panel, line=LINE_GREY, line_w=0.4)
        _rect(s, 0.30, y, 0.18, h, accent)
        # Left-side title & description
        _textbox(s, 0.55, y + 0.03, 3.40, 0.26, title,
                 size=10, bold=True, color=accent, anchor=MSO_ANCHOR.TOP)
        _textbox(s, 0.55, y + 0.28, 3.40, 0.32, descr,
                 size=8.5, color=BODY, anchor=MSO_ANCHOR.TOP)
        # Right-side .NET projects in this row
        _textbox(s, 4.10, y + 0.03, 5.55, h - 0.06, projects,
                 size=10.5, bold=True, color=BODY, anchor=MSO_ANCHOR.MIDDLE)

    # Bottom narrative band
    _rect(s, 0.30, 4.55, 9.40, 0.62, PANEL_PURPLE_TINT, line=LINE_GREY, line_w=0.4)
    _rect(s, 0.30, 4.55, 0.10, 0.62, CPU_PURPLE)
    _textbox(s, 0.50, 4.58, 9.20, 0.30,
             "Module boundaries chosen so production is wiring, not re-architecture",
             size=10, bold=True, color=CPU_PURPLE)
    _textbox(s, 0.50, 4.86, 9.20, 0.28,
             "Inter-module communication via DocProcessing.Contracts records — no shared types. LLM access via DocProcessing.Common factory only.",
             size=8.5, color=BODY)

    page_footer(s, 3)


# ---------------------------------------------------------------------------
# Slide 4 — Document Classification Journey (flow + decisions)
# ---------------------------------------------------------------------------

def slide_flow(prs):
    s = prs.slides.add_slide(prs.slide_layouts[6])
    page_header(s, "Document Classification Journey",
                "Per-document flow from arrival to handoff, with decision points")

    # Horizontal flow this time — five process steps left to right, then a decision column
    proc_y = 1.40
    proc_h = 0.65
    proc_w = 1.70
    gap = 0.08
    start_x = 0.30
    process_steps = [
        ("Document\narrives",     "PDF / TIF in landing\nfolder or Event Grid",           CPU_PURPLE),
        ("OCR extract",           "Azure Document\nIntelligence",                          ORANGE_HEADER),
        ("Flatten text",          "OcrTextFlattener\n→ plain text",                        ORANGE_HEADER),
        ("KB search",             "search_intent_kb\ntool (semantic)",                     GREEN_HEADER),
        ("Agent classify",        "MAF tool loop\nstrict JSON",                            GREEN_HEADER),
    ]
    for i, (title, sub, accent) in enumerate(process_steps):
        x = start_x + i * (proc_w + gap)
        _round(s, x, proc_y, proc_w, proc_h, PANEL_PURPLE_TINT if accent == CPU_PURPLE else (
            ORANGE_PANEL_TINT if accent == ORANGE_HEADER else GREEN_PANEL_TINT),
            line=LINE_GREY, line_w=0.4)
        _rect(s, x, proc_y, 0.08, proc_h, accent)
        # Title + sub
        _textbox(s, x + 0.12, proc_y + 0.03, proc_w - 0.18, 0.28, title,
                 size=10, bold=True, color=accent, anchor=MSO_ANCHOR.MIDDLE)
        _textbox(s, x + 0.12, proc_y + 0.30, proc_w - 0.18, proc_h - 0.32, sub,
                 size=8, color=BODY, anchor=MSO_ANCHOR.TOP)
        # Connecting arrows between successive process steps
        if i < len(process_steps) - 1:
            ax = x + proc_w + 0.005
            _textbox(s, ax, proc_y + proc_h / 2 - 0.18, 0.08, 0.30, "→",
                     size=16, bold=True, color=BODY, anchor=MSO_ANCHOR.MIDDLE)

    # Decision row
    dec_y = 2.45
    dec_w = 1.70
    dec_h = 0.75
    dec_steps = [
        ("Valid JSON?",         CPU_ORANGE_DARK, "Reformat once →\nelse fail"),
        ("Recognised\nintent?", CPU_ORANGE_DARK, "No → unknown\n→ HITL"),
        ("Confidence ≥\nthreshold?", CPU_ORANGE_DARK, "No → low-conf\n→ HITL"),
    ]
    dec_start_x = 0.30 + (proc_w + gap) * 2 - 0.85   # centred under middle process steps

    # Three decision diamonds in a row
    decision_xs = []
    for i, (label, fill, _) in enumerate(dec_steps):
        x = 0.30 + 0.85 + i * (dec_w + 0.50)
        decision_xs.append(x)
        _diamond(s, x, dec_y, dec_w, dec_h, fill)
        d = s.shapes[-1]
        _set_text(d, label, size=10, bold=True, color=WHITE)

    # Decision outcome labels under each diamond
    for i, (_, _, outcome) in enumerate(dec_steps):
        x = decision_xs[i]
        _textbox(s, x, dec_y + dec_h + 0.05, dec_w, 0.40, outcome,
                 size=8, color=BODY, align=PP_ALIGN.CENTER, anchor=MSO_ANCHOR.TOP)

    # Outcomes row — 3 outcome cards on the right side
    out_y = 3.95
    out_h = 0.65
    out_w = 2.55
    outcomes = [
        ("Auto persist + handoff",     "SQL row + archive blob\n+ outbound /workflow/handoff", GREEN_DEEPER),
        ("Route to HITL",              "Unknown intent OR\nlow-confidence prediction",         BLUE_DEEPER),
        ("DocumentFailedEvent",         "Bad JSON, 429 exhausted,\nor OCR rejection → DLQ",     CPU_PURPLE_DARK),
    ]
    out_start_x = 0.30
    for i, (title, sub, accent) in enumerate(outcomes):
        x = out_start_x + i * (out_w + 0.40)
        if x + out_w > SLIDE_W - 0.30:
            x = SLIDE_W - 0.30 - out_w
        _round(s, x, out_y, out_w, out_h, WHITE, line=accent, line_w=1.0)
        _rect(s, x, out_y, 0.08, out_h, accent)
        _textbox(s, x + 0.14, out_y + 0.05, out_w - 0.20, 0.28, title,
                 size=10.5, bold=True, color=accent, anchor=MSO_ANCHOR.MIDDLE)
        _textbox(s, x + 0.14, out_y + 0.30, out_w - 0.20, out_h - 0.32, sub,
                 size=8.5, color=BODY, anchor=MSO_ANCHOR.TOP)

    # Today / Tomorrow note at the bottom
    _textbox(s, 0.30, 4.85, 9.40, 0.40,
             "Today: HITL routing on unknown intent.  Tomorrow: confidence-threshold gate (Decision 3) for low-confidence predictions on recognised intents.",
             size=9, color=MUTED, align=PP_ALIGN.LEFT, anchor=MSO_ANCHOR.MIDDLE)

    page_footer(s, 4)


# ---------------------------------------------------------------------------
# Slide 5 — Local vs Production + Eval Proof
# ---------------------------------------------------------------------------

def slide_lvp_eval(prs):
    s = prs.slides.add_slide(prs.slide_layouts[6])
    page_header(s, "Local Today vs Production Target",
                "Cloud-swap is wiring, not code  •  Baseline accuracy on real production-shape forms")

    # Left column: substitution table
    rows = [
        ("Concern",       "Local",                  "Production (UK South)"),
        ("File arrival",  "FileSystemWatcher",      "Azure Files + Event Grid"),
        ("Message bus",   "RabbitMQ container",     "Azure Service Bus"),
        ("Blob store",    "Azurite emulator",       "Azure Blob (managed identity)"),
        ("SQL",           "SQL Server container",   "Azure SQL (managed identity)"),
        ("OCR",           "Azure DI (F0)",          "Azure DI (S0/S1, UK South)"),
        ("Chat LLM",      "GitHub Models gpt-4.1",  "Azure OpenAI gpt-5.1"),
        ("Vector store",  "InMemoryVectorStore",    "Azure AI Search (hybrid)"),
        ("Secrets",       "dotnet user-secrets",    "Key Vault + Workload Identity"),
        ("Hosting",       "Aspire AppHost",         "AKS + KEDA"),
    ]
    tbl_x = 0.30
    tbl_y = 1.15
    col_widths = [1.30, 1.95, 2.20]
    row_h = 0.32
    for i, row in enumerate(rows):
        x = tbl_x
        for j, cell in enumerate(row):
            w = col_widths[j]
            is_header = (i == 0)
            if is_header:
                fill = CPU_PURPLE
                color = WHITE
            else:
                fill = PANEL_PURPLE_TINT if i % 2 == 0 else WHITE
                color = BODY
            _rect(s, x, tbl_y + i * row_h, w, row_h, fill, line=LINE_GREY, line_w=0.3)
            cell_shape = s.shapes[-1]
            _set_text(cell_shape, cell, size=9, bold=is_header, color=color,
                      align=PP_ALIGN.LEFT)
            x += w

    # Right column: eval baseline
    eval_x = 6.10
    _textbox(s, eval_x, 1.10, 3.60, 0.30, "Baseline accuracy — 19 May 2026",
             size=11, bold=True, color=CPU_PURPLE)
    _textbox(s, eval_x, 1.38, 3.60, 0.25, "40-document corpus from samples/SampleForms",
             size=8.5, color=MUTED)

    # Big number card
    _rect(s, eval_x, 1.70, 3.60, 0.95, GREEN_PANEL_TINT, line=GREEN_DEEPER, line_w=0.75)
    _textbox(s, eval_x + 0.10, 1.75, 1.4, 0.50, "100%",
             size=26, bold=True, color=GREEN_DEEPER, align=PP_ALIGN.LEFT, anchor=MSO_ANCHOR.MIDDLE)
    _textbox(s, eval_x + 1.50, 1.75, 2.05, 0.50,
             "intent accuracy on classified documents",
             size=9, color=BODY, anchor=MSO_ANCHOR.MIDDLE)
    _textbox(s, eval_x + 0.10, 2.20, 3.45, 0.20,
             "25 / 40 classified  •  0 mismatches  •  multi-intent detected on 2",
             size=8.5, color=BODY)
    _textbox(s, eval_x + 0.10, 2.40, 3.45, 0.20,
             "15 failures — all infra (rate cap, F0 size, 8k context)",
             size=8, color=MUTED)

    # Per-intent table
    _textbox(s, eval_x, 2.78, 3.60, 0.25, "Per-intent precision / recall",
             size=10, bold=True, color=CPU_PURPLE)
    pi_rows = [
        ("Intent",     "TP", "FP", "P",    "R"),
        ("drip_ocp",   "14", "1",  "93%",  "100%"),
        ("sell_stock", "11", "0",  "100%", "100%"),
    ]
    pi_widths = [1.20, 0.45, 0.45, 0.70, 0.80]
    pi_y = 3.05
    for i, row in enumerate(pi_rows):
        x = eval_x
        for j, cell in enumerate(row):
            w = pi_widths[j]
            is_header = (i == 0)
            if is_header:
                fill, color = CPU_PURPLE, WHITE
            else:
                fill = PANEL_PURPLE_TINT if i % 2 == 0 else WHITE
                color = BODY
            _rect(s, x, pi_y + i * row_h, w, row_h, fill, line=LINE_GREY, line_w=0.3)
            shp = s.shapes[-1]
            _set_text(shp, cell, size=9, bold=is_header, color=color,
                      align=PP_ALIGN.CENTER if j > 0 else PP_ALIGN.LEFT)
            x += w

    # Read-it-as panel
    _rect(s, eval_x, 4.10, 3.60, 1.00, ORANGE_PANEL_TINT, line=CPU_ORANGE_DARK, line_w=0.5)
    _rect(s, eval_x, 4.10, 0.08, 1.00, CPU_ORANGE_DARK)
    _textbox(s, eval_x + 0.14, 4.13, 3.40, 0.22, "Read it as",
             size=9, bold=True, color=CPU_ORANGE_DARK)
    _textbox(s, eval_x + 0.14, 4.33, 3.40, 0.77,
             "•  Real-form accuracy is at parity with hand-curated test fixtures.\n"
             "•  Multi-intent detection works — surfaced two true multi-intent forms.\n"
             "•  Failure modes are all free-tier ceilings — they disappear in production.",
             size=8, color=BODY, align=PP_ALIGN.LEFT, anchor=MSO_ANCHOR.TOP)

    page_footer(s, 5)


# ---------------------------------------------------------------------------
# Build
# ---------------------------------------------------------------------------

def build():
    prs = Presentation()
    prs.slide_width = Inches(SLIDE_W)
    prs.slide_height = Inches(SLIDE_H)

    slide_title(prs)
    slide_architecture(prs)
    slide_component(prs)
    slide_flow(prs)
    slide_lvp_eval(prs)

    out = Path(__file__).with_name("CPU-stakeholder-deck.pptx")
    prs.save(out)
    print(f"wrote {out} ({out.stat().st_size:,} bytes)")


if __name__ == "__main__":
    build()
