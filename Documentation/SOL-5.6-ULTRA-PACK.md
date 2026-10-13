# GameManagement — UI Implementation Pack for Sol 5.6 Ultra

This package describes the **approved UI**, not a replacement app implementation.

## Contents
- `PROMPT_SOL_5_6_ULTRA.md` — paste into Sol 5.6 Ultra.
- `AGENTS.md` — strict project-level instructions; place in your repository root (merge carefully with any existing instructions).
- `DESIGN_SPEC.md` — complete measured design system, layout/interaction requirements, and QA conditions.
- `references/TARGET_dashboard_full_window.png` — cropped primary target, *no extraneous poster panels*.
- `references/TARGET_dashboard_main.png` — additional close-up target window.
- `references/TARGET_states_and_design_board.png` — reference states. Ignore mini previews as actual UI.
- `references/CURRENT_implementation_do_not_copy.png` — actual app screenshot; baseline to fix, **not a design to copy**.

## How to use
1. Extract this pack into a folder accessible to your coding agent, ideally your repository's project docs directory; make `AGENTS.md` discoverable at the repository root (merge rather than replacing existing instructions).
2. Provide the agent **access to your existing GameManagementApplication codebase**, not only images. It cannot actually integrate without the project.
3. Paste `PROMPT_SOL_5_6_ULTRA.md` into Sol 5.6 Ultra.
4. Ask it to audit and fix the real UI, run the app where possible, screenshot the result and compare against `references/TARGET_dashboard_full_window.png`.
5. Review the side-panel hover/pin behavior: unlike the static expanded screenshots, the panels are required to **overlay without main layout reflow**.

**Note:** Sizes in DESIGN_SPEC are design targets in logical pixels. Native framework / DPI requirements may necessitate minor changes for readability and Windows interaction standards. React component examples from 21st.dev can only be copied directly into a compatible UI stack.
