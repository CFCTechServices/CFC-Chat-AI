---
title: "CFC Beef Feeder Growout Model — RAG Edition"
source_file: "CFC Beef Model Guide.DOC"
version_date: "2007-11-09"
domain: "beef_feeding_model"
standards: ["NRC_1996", "NRC_2000_mods"]
chunk_tags: ["UI", "CONCEPT", "FORMULA", "CONSTRAINT"]
notes: |
  Equations and defaults are based on NRC 1996 with 2000 modifications. Software-specific constraints (e.g., max 1000 stored models, <=20 rations) are tagged as CONSTRAINT and should not be treated as domain truth.
---
## Using The Cfc Beef Feeder Growout Model
The CFC Beef Feeder Growout Model allows you to define and simulate a complete
growout model. All predictive equations, multiplier defaults, and adjustment factordefaults are from the NRC Nutrient Requirements of Beef Cattle, Seventh RevisedEdition, 1996, with NRC 2000 modifications.
The model’s straightforward design and total flexibility allows you to define:
- the desired energy multipliers and dry matter adjustment factors for their
respective predictive equations;
- the rations to be used and how they will be fed in the model;
- herd data;
- monetary data.
The growout is then simulated and provides summaries for performance, economics, total
ration usage, and the growout detail for every day the animals are on feed.
The beef system is linked to the Concept5 formulation system to allow any stored ration
within Concept5 to be used in the model.
The following pages will guide you through the set up and computation of the model, and
will provide a description of the solution summary.
From the menu bar on the Concept5 main menu, select Tools, then Beef Growout Model.
System Set-Up/Options
The main menu of the Beef Growout Model will open. Select the Growout Set-
Up/Options button.
System Set-Up/Options (cont.)
The General tab of the CFC Feeder Growout Options screen shows several frames. The
Your Company Identification allows you to enter your company name and address as youwould like it to appear on report headers.
The Printed Report Options frame offers several choices for the header print on all
printed reports – make the desired selections.
The Concept5 Nutrient Cross Reference frame links the energy nutrient numbers used in
Concept5 to the Beef Model system. Enter the nutrient number and units for both NEmand NEg from Concept5 in the boxes shown.
System Set-Up/Options (cont.)
The Maintenance Energy Required tab shows the NRC predictive equation for NEm and
all the multiplier defaults per the NRC.
Any multiplier default can be edited by selecting the appropriate textbox or cell and
typing in the desired numbers.
System Set-Up/Options (cont.)
The Dry Matter Intake Prediction tab shows the NRC predictive equations for DMI for
Growing Calves, Growing Yearlings, and Nonpregnant Beef Cows together with theiradjustment factors.
The adjustment factor NRC defaults are shown and, again, these can be edited by
selecting a textbox or cell and typing in the desired numbers.
Click the Exit button to return to the main menu.
Building a Model
Having established your system options, you are now ready to build a model. To begin,
click the Feeder Growout Model button.
Building a Model (cont.)
The screen header displays several buttons (some buttons are shown only when a
computed model is on screen):
- The Change Plant button allows you to switch to the desired plant within
Concept5.
- The Get Stored Model button opens the index of all saved models in the Beef
system for easy recall. The system will store up to 1000 separate models.
- The Add New Model button will clear all entries on all tabs to begin a fresh
model.
- The Save This Model button will save the model shown for later recall. And will
save any changes made to an existing model (shown only when a model code i.
- The Delete This Model deletes the model shown on screen.
- The Compute Growout button computes the model and creates the solution
summaries.
- The Print Reports button opens an index of the report options available for the
solution.
Building a Model (cont.)
When the Feeder Growout Model button on the main menu is selected, the screen shown
below will appear. On the Growout Model Description tab, enter a model code and nameand, optionally, the name and address of the recipient. The code maximum is eightalphanumeric digits; the name max is 40 characters. The recipient name will appear on allreports for this model.
If you want to edit an existing model, click the Get Stored Model button and choose from
the index.
Building a Model (cont.)
The Growout Model Set-Up tab contains all the data that will be used when the model is
computed.
In the Herd Data frame, enter the desired information in the textboxes using the scrollbaror typing in the data. Begin Shrink, End Shrink, and Death Loss are optional entries.Additional optional entries allow you to adjust for Bulls, Anabolic Stimulant,Temperature, and /or Mud. Begin Weight, End Weight, and Number of Head arerequired.
The Monetary Data frame allows you to account for all extra costs associated with the
model. Only the Buying Price and Selling Price require an entry.
- Feed MarkUp: you may enter an additional markup to the feed costs entered in the
Cost column of the Ration Composition and Feeding Schedule shown at the  <!-- TAG: UI -->
bottom of the screen.
- Yardage Costs are entered as $/Head/Day
- Veterinary Costs: costs can be separated as Service and as Medicine.
- Hauling as $/Head: you can separate the costs to the Lot and/or to Slaughter.
- Commission and Miscellaneous charges can be listed as $/Head.
- Interest Rate can be applied On Feed and /or on Cattle. The percentage entered in
the textbox is an APR. The total interest dollar amount used in the summary
calculation is based on the days in the growout period.
- Buying Price as $/CWT – this is a required entry.
- Selling Price as $/CWT – this is a required entry.
Building a Model (cont.)
The Ration Composition and Feeding Schedule frame is where you specify the rations to  <!-- TAG: UI -->
be used in the model. They may be chosen from the existing Concept5 rations by clickingthe Pick C5 Ration button and choosing from the index (click in the Ration Code cell tomake the button appear).. You may also define the rations on each row by entering aCode, Description, Cost, Dry Matter Percent, NEm, and NEg. All except the last rationspecified for use must also show a value in either the #Days on Feed column, or the UntilBody Wt column. The Until Body Weight column designates the animal weight at whichthe ration will no longer be fed. You may combine both types of rations in the samemodel. Up to 20 rations can be used for each model.
After all the desired data has been entered, click the Compute Growout button to simulate
the model.
Model Summaries
The model will compute and the screen will open to the Performance/Economic
Summary tab of the Growout Summary tab.
The Performance Summary is shown in the frame on the left. Starting with the beginningweight (with any specified shrink) and the ending weight (without any specified shrink),all the pertinent performance pieces of the model are listed.
The Economic Summary is shown in the frame on the right.
The Profit $/Head is calculated as follows:  <!-- TAG: FORMULA -->
1) The ending weight, minus shrink, is multiplied by the Sell Price.
2) The beginning weight, minus shrink, is multiplied by the Buying Price. To
this number the Total Cost Per Head is added.
3) Subtract 2) from 1) to get Profit $/Head.  <!-- TAG: FORMULA -->
The Breakeven Selling $/CWT is calculated as follows:  <!-- TAG: FORMULA -->
The beginning weight (as CWT), minus shrink, is multiplied by the buying price(per CWT). To this number the Total Cost Per Head is added. Divide this sum bythe ending weight (as CWT), minus shrink, to get the Breakeven Selling $/CWT.  <!-- TAG: FORMULA -->
The Breakeven Purchase $/CWT is calculated as follows:  <!-- TAG: FORMULA -->
The ending weight (as CWT), minus shrink, is multiplied by the selling price (perCWT). From this number the Total Cost Per Head is subtracted. Divide this sumby the beginning weight (as CWT), minus shrink, to get the Breakeven Purchase$/CWT.  <!-- TAG: FORMULA -->
Model Summaries (cont.)
The $Return per $ Feed Cost is calculated as: [(Profit $/Head)/(Total Feed Cost per  <!-- TAG: FORMULA -->
Head)] + 1.
The $Return per $ Invested is calculated as: [(Profit $/Head)/(Total Cost)] + 1. Total Cost  <!-- TAG: FORMULA -->
in this calculation includes the purchase costs plus the Total Cost per Head.
Model Summaries (cont.)
The Ration Usage Summary tab shows the schedule for the entire feeding period by
individual ration.
Model Summaries (cont.)
The Growout Daily Detail tab shows the feeding results for each day of the feeding
period. Guided by the predictive equations, the necessary feed intake, NEm, and NEg areall listed on a daily basis.
The model can be saved by clicking the Save This Model button, and recalled by clickingthe Get Stored Model button and choosing from the index. Recalled models can be editedand saved again by clicking the Save This Model button.
Click the Print Reports button to open the reports menu (see following page).
Model Summaries (cont.)
Selecting the Print Reports button opens the Printed Report Options popup. Choose any
option to open a soft print of the report. Shown is the top portion of the Performance andFinancial Summary.
Model Summaries (cont.)
Click the Printer icon to execute a print. Or choose the Print to File button (to store as a
.txt file), or the Copy To Clipboard button for subsequent pasting into the desiredapplication.
Model Summaries (cont.)
One of the report choices is the Graphical Breakeven Summary. This report analyzes the  <!-- TAG: FORMULA -->
data input at various sell prices and purchase (buy) costs, and graphs the results for easyinterpretation. The report is shown below.

## Glossary
- **NEg**: Net Energy for Gain  
- **NEm**: Net Energy for Maintenance  
- **DMI**: Dry Matter Intake  
- **$/CWT**: Dollars per hundredweight  
- **$/Head/Day**: USD per head per day  

## Software Constraints  <!-- TAG: CONSTRAINT -->
- Stored models: up to **1000** per system.
- Rations per model: up to **20**.
- Model code: max **8** alphanumeric; model name: max **40** chars.

## Figures — Semantic Text From Images

> The following entries convert each screenshot/figure into **retrievable text**. Each figure has alt‑text and a structured summary of what’s visible (buttons, fields, columns, and constraints).

### FIG‑01 — Launch Path from Concept5 Main Menu *(Page 2)*
**Alt‑text:** Concept5 main menu with menu bar; Tools → Beef Growout Model highlighted.  
**Derived text:** From the Concept5 main window, open **Tools** → **Beef Growout Model** to launch the module.

### FIG‑02 — Beef Growout Main Menu *(Page 3)*
**Alt‑text:** Beef Growout Model main screen with button set; **Growout Set‑Up/Options** visible.  
**Derived text (UI elements):** Buttons include: `Growout Set‑Up/Options`, `Feeder Growout Model` (to build/run), and other navigation items for the module.

### FIG‑03 — Options: General Tab *(Page 4)*
**Alt‑text:** “CFC Feeder Growout Options — General” tab showing frames: **Your Company Identification**, **Printed Report Options**, **Concept5 Nutrient Cross Reference**.  
**Derived text:**  
- `Your Company Identification`: free‑text fields for company name/address (used on report headers).  
- `Printed Report Options`: checkboxes to control header printing across reports.  
- `Concept5 Nutrient Cross Reference`: textboxes for **NEm** and **NEg** nutrient numbers/units used by Concept5 (maps formulation nutrients to the Beef Model).

### FIG‑04 — Options: Maintenance Energy Required *(Page 5)*
**Alt‑text:** “Maintenance Energy Required” tab with NRC NEm predictive equation and an editable multipliers grid.  
**Derived text:**  
- Shows the NRC predictive **NEm** equation (reference standard) and default multiplier values.  
- Each multiplier is editable in‑place (textbox/cell). *(These are software settings; changing them alters model behavior.)*

### FIG‑05 — Options: Dry Matter Intake Prediction *(Page 6)*
**Alt‑text:** “Dry Matter Intake Prediction” tab with equations for **Growing Calves**, **Growing Yearlings**, **Nonpregnant Beef Cows**, plus an adjustment‑factors grid.  
**Derived text:**  
- Displays NRC DMI equations per class (3 categories).  
- Adjustment factors presented in a table; defaults shown and editable cell‑by‑cell.

### FIG‑06 — Enter Module *(Page 7)*
**Alt‑text:** Main menu with **Feeder Growout Model** button highlighted.  
**Derived text:** Click `Feeder Growout Model` to build or edit a growout scenario.

### FIG‑07 — Growout Model Description Tab *(Page 9)*
**Alt‑text:** Description tab with fields **Model Code**, **Model Name**, **Recipient Name/Address**; includes `Get Stored Model`.  
**Derived text:**  
- `Model Code` (max **8** alphanumeric).  
- `Model Name` (max **40** chars).  
- Optional recipient info (appears on model reports).  
- Button: `Get Stored Model` opens index of saved models (system stores up to **1000** models).

### FIG‑08 — Growout Model Set‑Up Tab *(Page 10)*
**Alt‑text:** Set‑Up tab showing **Herd Data** and **Monetary Data** frames with numerous inputs and checkboxes.  
**Derived text (fields):**  
- **Herd Data**: `Begin Weight` (req), `End Weight` (req), `Number of Head` (req), optional `Begin Shrink`, `End Shrink`, `Death Loss`, adjustments: `Bulls`, `Anabolic Stimulant`, `Temperature`, `Mud`.  
- **Monetary Data**: `Feed MarkUp` (applies to ration **Cost**), `Yardage ($/Head/Day)`, `Veterinary: Service/Medicine`, `Hauling ($/Head)` to Lot/Slaughter, `Commission`, `Miscellaneous ($/Head)`, `Interest Rate` (**APR**) applied `On Feed` and/or `On Cattle`, **Buying Price ($/CWT)** *(required)*, **Selling Price ($/CWT)** *(required)*.

### FIG‑09 — Ration Composition & Feeding Schedule *(Page 11)*
**Alt‑text:** Grid with columns **Ration Code**, **Description**, **Cost**, **DM %**, **NEm**, **NEg**, **# Days on Feed**, **Until Body Wt**; button **Pick C5 Ration** appears in Ration Code cell.  
**Derived text:**  
- You may pick a stored Concept5 ration (`Pick C5 Ration`) or manually enter: `Code`, `Description`, `Cost`, `DM%`, `NEm`, `NEg`.  
- For every ration except the last, supply **either** `# Days on Feed` **or** `Until Body Wt` (cutoff weight for ration).  
- Up to **20** rations can be listed per model.

### FIG‑10 — Performance & Economic Summary *(Page 12)*
**Alt‑text:** Growout Summary screen with left **Performance** frame and right **Economic** frame.  
**Derived text:**  
- Performance lists begin/end weights (after shrink rules) and calculated performance metrics.  
- Economics lists Profit, Breakeven (sell/purchase), returns per feed cost and per total invested.  
- Formulas implemented:  
  - **Profit $/Head** = (End Wt – shrink)*Sell Price − [(Begin Wt – shrink)*Buy Price + Total Cost/Head]  
  - **Breakeven Sell $/CWT** = [(Begin Wt(CWT) – shrink)*Buy Price + Total Cost/Head] ÷ [End Wt(CWT) – shrink]  
  - **Breakeven Purchase $/CWT** = [(End Wt(CWT) – shrink)*Sell Price − Total Cost/Head] ÷ [Begin Wt(CWT) – shrink]  
  - **$Return per $ Feed** = (Profit/Total Feed Cost) + 1; **$Return per $ Invested** = (Profit/Total Cost) + 1.

### FIG‑11 — Ration Usage Summary *(Page 14)*
**Alt‑text:** Report‑style table summarizing feed schedule by ration for the entire period.  
**Derived text:** Per‑ration totals/schedule over the feeding horizon; intended for quick inventory and scheduling checks.

### FIG‑12 — Growout Daily Detail *(Page 15)*
**Alt‑text:** Daily results grid for the feeding period with per‑day intake and energy values.  
**Derived text:** Lists **daily** predicted values (incl. **DMI**, **NEm**, **NEg**) and other day‑level results driven by the predictive equations.

### FIG‑13 — Printed Report Options *(Page 16)*
**Alt‑text:** “Printed Report Options” popup with tickboxes; example shows top portion of Performance & Financial Summary.  
**Derived text:** Choose report(s) to soft‑print/preview; options respect the global **Printed Report Options** from the General tab.

### FIG‑14 — Print/Export Controls *(Page 17)*
**Alt‑text:** Toolbar with Printer icon, **Print to File (.txt)**, and **Copy To Clipboard** controls.  
**Derived text:**  
- `Printer` prints the active report.  
- `Print to File` emits a `.txt` output.  
- `Copy To Clipboard` copies the report text for pasting elsewhere.

### FIG‑15 — Graphical Breakeven Summary *(Page 18)*
**Alt‑text:** Chart analyzing multiple **sell prices** and **purchase costs** scenarios, visualizing profit/breakeven sensitivity.  
**Derived text:** Report evaluates the input scenario over varying sell/purchase price points and provides a graph for comparison; use alongside numeric breakeven formulas above.
