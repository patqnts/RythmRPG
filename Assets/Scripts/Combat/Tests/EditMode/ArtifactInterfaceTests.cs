using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RythmRPG.Core;
using RythmRPG.UI.Title;
using TMPro;
using UnityEngine;

namespace RythmRPG.Combat.Tests
{
    public sealed class ArtifactInterfaceTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly List<Object> created = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = created.Count - 1; i >= 0; i--)
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            created.Clear();
        }

        private LoadoutPanel Inventory(out RunBuildState build)
        {
            var strike = new AbilityDefinition.Builder("ui-strike", "Strike").Cost(0).Build();
            var spell = new AbilityDefinition.Builder("ui-spell", "Spell").Cost(8).Build();
            var reserve = new AbilityDefinition.Builder("ui-reserve", "Reserve").Cost(12).Build();
            created.AddRange(new Object[] { strike, spell, reserve });
            build = new RunBuildState();
            build.AddAbility(strike, 0);
            build.AddAbility(spell, 1);
            build.AddAbility(reserve, -2);
            var panel = LoadoutPanel.CreateTemplate(null);
            created.Add(panel.gameObject);
            Set(panel, "build", build);
            Set(panel, "editable", true);
            Set(panel, "open", true);
            var artifact = ArtifactInterfaceView.Ensure(panel.gameObject);
            Set(panel, "artifact", artifact);
            Call(panel, "Rebuild");
            return panel;
        }

        [Test]
        public void InventoryRearrange_KeepsReserveAndSelectedInspectionInSync()
        {
            var panel = Inventory(out RunBuildState build);
            var reserve = build.Reserve.Single();
            // Four slot rows followed by the reserve. Choose it, then place it in slot two.
            Set(panel, "selected", 4);
            Call(panel, "Confirm");
            Set(panel, "selected", 1);
            Call(panel, "Confirm");
            Assert.That(build.GetSlot(1), Is.SameAs(reserve));
            Assert.That(build.Reserve.Single().Definition.Id, Is.EqualTo("ui-spell"));
            Assert.That(Get<TMP_Text>(panel, "detailTitle").text, Does.Contain("Reserve"));
            Assert.That(Get<IList>(panel, "items").Count, Is.EqualTo(5), "Rebuilding cannot duplicate rows.");
        }

        [Test]
        public void InventoryLongInspection_IsScrollableInsteadOfTruncated()
        {
            var panel = Inventory(out _);
            TMP_Text body = Get<TMP_Text>(panel, "detailBody");
            var scroll = Get<UnityEngine.UI.ScrollRect>(panel, "detailScroll");
            Assert.That(body.gameObject.activeInHierarchy, Is.True);
            Assert.That(scroll.content, Is.SameAs(body.rectTransform));
            Assert.That(body.overflowMode, Is.EqualTo(TextOverflowModes.Overflow));
            body.text = string.Join("\n", Enumerable.Repeat("An artifact memory with a long description.", 50));
            body.ForceMeshUpdate();
            Canvas.ForceUpdateCanvases();
            Assert.That(body.preferredHeight, Is.GreaterThan(scroll.viewport.rect.height));
            Assert.That(scroll.horizontal, Is.False);
        }

        [Test]
        public void ClosingMidReveal_DoesNotJumpToFullyVisible()
        {
            var panel = Inventory(out _);
            var artifact = panel.GetComponent<ArtifactInterfaceView>();
            artifact.RefreshEffects();
            artifact.SetVisibility(0.2f);
            panel.GetComponent<CanvasGroup>().alpha = 0.6f;
            panel.Close();
            Call(panel, "Update");
            Assert.That(artifact.Visibility, Is.InRange(0f, 0.2f));
            Assert.That(panel.GetComponent<CanvasGroup>().blocksRaycasts, Is.False);
            Set(panel, "fadeAt", GamePause.UnpausedRealtime - 2f);
            Call(panel, "Update");
            Assert.That(Get<bool>(panel, "open"), Is.False);
        }

        [Test]
        public void Reveal_DissolvesOnePanelAndMasksContentsWithoutChangingTextMaterials()
        {
            var panel = Inventory(out _);
            var artifact = panel.GetComponent<ArtifactInterfaceView>();
            TMP_Text text = Get<TMP_Text>(panel, "detailBody");
            Material original = text.font.material;
            Shader originalShader = original.shader;
            Material textMaterial = text.fontSharedMaterial;
            artifact.RefreshEffects();
            artifact.SetVisibility(0.4f);
            var surface = panel.transform.Find("Panel");
            var mask = surface.GetComponent<UnityEngine.UI.Mask>();
            TitleLogoImage effect = surface.GetComponent<TitleLogoImage>();
            Assert.That(effect, Is.Not.Null);
            Assert.That(mask.MaskEnabled(), Is.True);
            Assert.That(text.GetComponentInParent<UnityEngine.UI.Mask>(), Is.SameAs(mask));
            Assert.That(panel.GetComponentsInChildren<TitleLogoBase>(true).Length, Is.EqualTo(1));
            Assert.That(effect.Disintegrate, Is.EqualTo(0.6f).Within(0.0001f));
            Assert.That(text.font.material, Is.SameAs(original));
            Assert.That(original.shader, Is.SameAs(originalShader));
            Assert.That(text.fontSharedMaterial, Is.SameAs(textMaterial));
            typeof(TitleLogoBase).GetMethod("LateUpdate", Private).Invoke(effect, null);
            var graphic = surface.GetComponent<UnityEngine.UI.Image>();
            Assert.That(graphic.materialForRendering.GetFloat("_Disintegrate"), Is.EqualTo(0.6f).Within(0.0001f));
            Assert.That(graphic.materialForRendering.IsKeywordEnabled("UNITY_UI_ALPHACLIP"), Is.True);
            Assert.That(graphic.canvasRenderer.GetPopMaterial(0).GetFloat("_Disintegrate"), Is.EqualTo(0.6f).Within(0.0001f));
            artifact.SetVisibility(0.8f);
            typeof(TitleLogoBase).GetMethod("LateUpdate", Private).Invoke(effect, null);
            Assert.That(graphic.materialForRendering.GetFloat("_Disintegrate"), Is.EqualTo(0.2f).Within(0.0001f));
        }

        [Test]
        public void RewardReopening_ClaimsOnceAndKeepsDetailsScrollable()
        {
            var screen = RewardSelectionScreen.CreateTemplate(null);
            created.Add(screen.gameObject);
            var build = new RunBuildState();
            var registry = new BuildContentRegistry();
            var offer = new RewardOfferData { offerId = "ui-offer", sourceKey = "ui-preview" };
            offer.options.Add(new RewardOptionData { optionId = "ui-growth", kind = RewardKind.Growth, contentId = GrowthRewards.Mana });
            Set(screen, "build", build);
            Set(screen, "registry", registry);
            Set(screen, "offer", offer);
            Call(screen, "BuildCards");
            Call(screen, "BuildCards");
            var cards = screen.GetComponentsInChildren<RewardCardView>(false);
            Assert.That(cards.Length, Is.EqualTo(1));
            Assert.That(cards[0].Root.GetComponentInChildren<UnityEngine.UI.ScrollRect>(), Is.Null);
            Assert.That(Get<UnityEngine.UI.ScrollRect>(screen, "selectionScroll"), Is.Not.Null);
            var mask = screen.transform.Find("Projection").GetComponent<UnityEngine.UI.Mask>();
            Assert.That(cards[0].GetComponentInParent<UnityEngine.UI.Mask>(true), Is.SameAs(mask));
            Assert.That(screen.GetComponentsInChildren<TitleLogoBase>(true).Length, Is.EqualTo(1));
            Call(screen, "ConfirmCard");
            int bonus = build.BonusMaxMana;
            Assert.That(offer.claimed, Is.True);
            Assert.That(bonus, Is.GreaterThan(0));
            Call(screen, "ConfirmCard");
            Assert.That(build.BonusMaxMana, Is.EqualTo(bonus));
        }

        [Test]
        public void EquippedGrid_EmphasizesSlotsAndNavigatesInTheirVisualDirection()
        {
            var panel = Inventory(out _);
            var equipped = Get<RectTransform>(panel, "equippedContent");
            var reserve = Get<RectTransform>(panel, "abilityContent");
            Assert.That(equipped.childCount, Is.EqualTo(4));
            Assert.That(equipped.GetChild(0).GetComponent<RectTransform>().rect.height,
                Is.GreaterThan(reserve.GetChild(0).GetComponent<RectTransform>().rect.height * 3f));
            Set(panel, "selected", 0);
            Call(panel, "Navigate", Vector2.right);
            Assert.That(Get<int>(panel, "selected"), Is.EqualTo(1));
            Call(panel, "Navigate", Vector2.down);
            Assert.That(Get<int>(panel, "selected"), Is.EqualTo(3));
            Call(panel, "Navigate", Vector2.left);
            Assert.That(Get<int>(panel, "selected"), Is.EqualTo(2));
        }

        [Test]
        public void InventoryTerms_AreColouredLinksAndOpenPlainLanguageDefinitions()
        {
            var panel = Inventory(out _);
            Set(panel, "selected", 1); // the 8 MP ability
            Call(panel, "UpdateDetails");
            TMP_Text body = Get<TMP_Text>(panel, "detailBody");
            Assert.That(body.text, Does.Contain("<link=\"mana\">").And.Contain("<color=#69D9FF>MP</color>"));
            Assert.That(body.raycastTarget, Is.True, "Glossary links must receive pointer clicks.");

            Call(panel, "InspectTerm", "mana");
            Assert.That(Get<TMP_Text>(panel, "detailTitle").text, Does.Contain("MP / Mana"));
            Assert.That(body.text, Does.Contain("resource paid when an ability is committed"));
            Assert.That(Get<bool>(panel, "inspectingTerm"), Is.True);

            Call(panel, "ExitTermInspection");
            Assert.That(body.text, Does.Contain("<link=\"mana\">"));
            Assert.That(Get<bool>(panel, "inspectingTerm"), Is.False);
        }

        [Test]
        public void Glossary_RecognizesLongTermsBeforeTheirShortForms()
        {
            string formatted = GameplayGlossary.Format("A closing phrase earns counter charges and shield.", out List<GameplayTerm> found);
            Assert.That(formatted, Does.Contain("<link=\"phrase\">").And.Contain("<link=\"counter\">").And.Contain("<link=\"shield\">"));
            Assert.That(found.Select(term => term.Id), Is.EqualTo(new[] { "phrase", "counter", "shield" }));
            Assert.That(GameplayGlossary.Find("counter").Definition, Does.Contain("stored charge"));
        }

        [Test]
        public void ModalArchive_CanCaptureLaneInputWithoutDisablingSystemControls()
        {
            var owner = new object();
            try
            {
                GameInput.BlockLaneInput(owner);
                Assert.That(GameInput.IsLaneInputBlocked, Is.True);
                Assert.That(GameInput.Loadout, Is.Not.Null);
            }
            finally
            {
                GameInput.UnblockLaneInput(owner);
            }
            Assert.That(GameInput.IsLaneInputBlocked, Is.False);
        }

        [Test]
        public void Rewards_ShareOneScrollableInfoPaneThatFollowsSelection()
        {
            var screen = RewardSelectionScreen.CreateTemplate(null);
            created.Add(screen.gameObject);
            var offer = new RewardOfferData { offerId = "ui-info", sourceKey = "ui-preview" };
            offer.options.Add(new RewardOptionData { optionId = "hp", kind = RewardKind.Growth, contentId = GrowthRewards.Health });
            offer.options.Add(new RewardOptionData { optionId = "mp", kind = RewardKind.Growth, contentId = GrowthRewards.Mana });
            Set(screen, "build", new RunBuildState());
            Set(screen, "registry", new BuildContentRegistry());
            Set(screen, "offer", offer);
            Call(screen, "BuildCards");
            var body = Get<TMP_Text>(screen, "selectionBody");
            var scroll = Get<UnityEngine.UI.ScrollRect>(screen, "selectionScroll");
            var grid = Get<RectTransform>(screen, "cardContainer").GetComponent<UnityEngine.UI.GridLayoutGroup>();
            Assert.That(grid.constraint, Is.EqualTo(UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount));
            Assert.That(grid.constraintCount, Is.EqualTo(1));
            Assert.That(grid.cellSize.x, Is.GreaterThan(Get<RectTransform>(screen, "selectionPanel").rect.width * 1.5f));
            Assert.That(screen.GetComponentsInChildren<UnityEngine.UI.ScrollRect>(false).Length, Is.EqualTo(1));
            Assert.That(body.text, Does.Contain("HP"));
            Call(screen, "Select", 1);
            Assert.That(body.text, Does.Contain("MP"));
            Assert.That(offer.claimed, Is.False, "Inspecting a choice must not claim it.");
            Assert.That(scroll.content, Is.SameAs(body.rectTransform));
            body.text = string.Join("\n", Enumerable.Repeat("A long reward description.", 50));
            Canvas.ForceUpdateCanvases();
            Assert.That(body.preferredHeight, Is.GreaterThan(scroll.viewport.rect.height));
            Call(screen, "Select", 0);
            Assert.That(body.text, Does.Contain("HP"));
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(0f).Within(.01f));
        }

        [Test]
        public void Rewards_KeyboardNavigationWrapsWithoutClaimingAndPokesSelectedChoice()
        {
            var screen = RewardSelectionScreen.CreateTemplate(null);
            created.Add(screen.gameObject);
            var offer = new RewardOfferData { offerId = "ui-navigation", sourceKey = "ui-preview" };
            offer.options.Add(new RewardOptionData { optionId = "hp", kind = RewardKind.Growth, contentId = GrowthRewards.Health });
            offer.options.Add(new RewardOptionData { optionId = "mp", kind = RewardKind.Growth, contentId = GrowthRewards.Mana });
            Set(screen, "build", new RunBuildState());
            Set(screen, "registry", new BuildContentRegistry());
            Set(screen, "offer", offer);
            Call(screen, "BuildCards");
            Call(screen, "NavigateCards", -1);
            Assert.That(Get<int>(screen, "selected"), Is.EqualTo(1));
            Call(screen, "ApplyCards", 0f, true);
            var choices = screen.GetComponentsInChildren<RewardCardView>(false);
            Assert.That(choices[1].Root.GetComponent<ArtifactSlimeReaction>().IsReacting, Is.True);
            Assert.That(choices[1].Root.Find("Prismatic Frame").GetComponent<ArtifactGeometry>().ReactionAmount, Is.GreaterThan(0f));
            Assert.That(choices[0].Root.Find("Prismatic Frame").GetComponent<ArtifactGeometry>().ReactionAmount, Is.Zero);
            Assert.That(screen.transform.Find("Projection/Artifact Frame"), Is.Null);
            Assert.That(screen.GetComponentsInChildren<ArtifactGeometry>().Where(g => g.name == "Info Frame").All(g => g.ReactionAmount == 0f), Is.True);
            Call(screen, "NavigateCards", 1);
            Call(screen, "ApplyCards", 0f, true);
            Assert.That(Get<int>(screen, "selected"), Is.Zero);
            Assert.That(choices[1].Root.Find("Prismatic Frame").GetComponent<ArtifactGeometry>().ReactionAmount, Is.Zero);
            Assert.That(offer.claimed, Is.False);
            Assert.That(RewardSelectionStyle.LoadOrDefault().PreviousKeys, Does.Contain(KeyCode.UpArrow));
            Assert.That(RewardSelectionStyle.LoadOrDefault().PreviousKeys, Does.Contain(KeyCode.W));
            Assert.That(RewardSelectionStyle.LoadOrDefault().NextKeys, Does.Contain(KeyCode.DownArrow));
            Assert.That(RewardSelectionStyle.LoadOrDefault().NextKeys, Does.Contain(KeyCode.S));
        }

        [Test]
        public void InventoryPoke_DissolvesWholeSelectedItemAndNeverRegistersDuringGraphicRebuild()
        {
            var warnings = new List<string>();
            Application.LogCallback capture = (message, stack, type) =>
            {
                if (message.ToLowerInvariant().Contains("graphic rebuild")) warnings.Add(message);
            };
            Application.logMessageReceived += capture;
            try
            {
                var panel = Inventory(out _);
                panel.GetComponent<Canvas>().enabled = true;
                panel.GetComponent<CanvasGroup>().alpha = 1f;
                Call(panel, "Rebuild");
                Canvas.ForceUpdateCanvases();
                Call(panel, "Select", 1);
                Canvas.ForceUpdateCanvases();
                var outer = panel.transform.Find("Panel/Prismatic Frame").GetComponent<ArtifactGeometry>();
                Assert.That(outer.ReactionAmount, Is.Zero);
                Assert.That(outer.material.shader.name, Is.Not.EqualTo("RythmRPG/UI/Artifact Living Outline"));
                // Use the item model so the assertion also works with authored container names.
                var item = Get<IList>(panel, "items")[1];
                var selectedRow = (RectTransform)item.GetType().GetField("Row").GetValue(item);
                selectedRow.GetComponent<ArtifactSlimeReaction>().Advance(ArtifactInterfaceStyle.Load().pokeDisintegrateSeconds);
                var selectedFrame = selectedRow.Find("Row Tracery").GetComponent<ArtifactGeometry>();
                Assert.That(selectedFrame.ReactionAmount, Is.GreaterThan(0f));
                Assert.That(selectedFrame.materialForRendering.GetFloat("_PokeAmount"), Is.GreaterThan(0f));
                var itemMask = selectedRow.GetComponent<UnityEngine.UI.Mask>();
                Assert.That(itemMask, Is.Not.Null);
                Assert.That(selectedRow.GetComponent<ArtifactComponentMask>(), Is.Not.Null);
                var itemGraphic = selectedRow.GetComponent<UnityEngine.UI.Image>();
                Assert.That(itemGraphic.materialForRendering.GetFloat("_ComponentSurface"), Is.EqualTo(1f));
                Assert.That(itemGraphic.materialForRendering.GetFloat("_Disintegrate"), Is.GreaterThan(0f));
                foreach (var text in selectedRow.GetComponentsInChildren<TMP_Text>())
                    Assert.That(text.GetComponentInParent<UnityEngine.UI.Mask>(), Is.SameAs(itemMask));
                var icon = selectedRow.Find("Icon");
                Assert.That(icon.GetComponentInParent<UnityEngine.UI.Mask>(), Is.SameAs(itemMask));
                var rows = panel.GetComponentsInChildren<ArtifactGeometry>().Where(g => g.name == "Row Tracery" && g != selectedFrame);
                foreach (var row in rows)
                {
                    Assert.That(row.ReactionAmount, Is.Zero);
                }
                Call(panel, "Rebuild");
                Canvas.ForceUpdateCanvases();
                Assert.That(warnings, Is.Empty);
            }
            finally { Application.logMessageReceived -= capture; }
        }

        [Test]
        public void RewardPoke_DissolvesAllContentsUnderOneItemMaskAndReforms()
        {
            var screen = RewardSelectionScreen.CreateTemplate(null);
            created.Add(screen.gameObject);
            var offer = new RewardOfferData { offerId = "ui-component", sourceKey = "ui-preview" };
            offer.options.Add(new RewardOptionData { optionId = "hp", kind = RewardKind.Growth, contentId = GrowthRewards.Health });
            offer.options.Add(new RewardOptionData { optionId = "mp", kind = RewardKind.Growth, contentId = GrowthRewards.Mana });
            Set(screen, "build", new RunBuildState());
            Set(screen, "registry", new BuildContentRegistry());
            Set(screen, "offer", offer);
            Call(screen, "BuildCards");
            Call(screen, "Select", 1);
            Call(screen, "ApplyCards", 0f, true);
            Canvas.ForceUpdateCanvases();
            var root = screen.GetComponentsInChildren<RewardCardView>(false)[1].Root;
            root.GetComponent<ArtifactSlimeReaction>().Advance(ArtifactInterfaceStyle.Load().pokeDisintegrateSeconds);
            var mask = root.GetComponent<UnityEngine.UI.Mask>();
            Assert.That(mask, Is.Not.Null);
            Assert.That(mask.showMaskGraphic, Is.False, "The mask must preserve the authored reward background.");
            foreach (string part in new[] { "Background", "Icon", "Title", "Prismatic Frame" })
                Assert.That(root.Find(part).GetComponentInParent<UnityEngine.UI.Mask>(true), Is.SameAs(mask));
            var graphic = root.GetComponent<UnityEngine.UI.Image>();
            Assert.That(graphic.materialForRendering.GetFloat("_Disintegrate"), Is.GreaterThan(0f));
            root.GetComponent<ArtifactSlimeReaction>().Advance(2f);
            Assert.That(graphic.materialForRendering.GetFloat("_Disintegrate"), Is.Zero);
            Assert.That(offer.claimed, Is.False);
        }

        [Test]
        public void EdgeDisintegration_PlaysIntactThenDisintegrateThenRestoreWithEditableTiming()
        {
            var root = new GameObject("Edge pulse", typeof(RectTransform));
            created.Add(root);
            var rect = (RectTransform)root.transform;
            rect.sizeDelta = new Vector2(400f, 200f);
            ArtifactInterfaceView.Decorate("Outline", rect, ArtifactGeometry.Shape.Frame, Color.white);
            var response = ArtifactSlimeReaction.Ensure(rect);
            var settings = ScriptableObject.CreateInstance<ArtifactInterfaceStyle>();
            created.Add(settings);
            settings.wobbleSeconds = .1f;
            settings.pokeDisintegrateSeconds = .2f;
            settings.pokeGlitchSeconds = .4f;
            settings.pokeGlitchStrength = .8f;
            settings.pokeEdgeDepthPixels = 20f;
            Set(response, "style", settings);
            response.Poke();
            var material = root.GetComponent<UnityEngine.UI.Image>().materialForRendering;
            Assert.That(material.GetFloat("_Disintegrate"), Is.Zero, "Start intact.");
            response.Advance(.1f);
            float partial = material.GetFloat("_Disintegrate");
            Assert.That(partial, Is.GreaterThan(0f));
            response.Advance(.2f);
            float peak = material.GetFloat("_Disintegrate");
            Assert.That(peak, Is.GreaterThan(partial));
            Assert.That(material.GetFloat("_EdgeDepth"), Is.EqualTo(20f));
            Assert.That(response.IsReacting, Is.True, "Restoration must continue after a shorter wobble finishes.");
            response.Advance(.4f);
            Assert.That(material.GetFloat("_Disintegrate"), Is.InRange(.001f, peak - .001f));
            response.Advance(.61f);
            Assert.That(material.GetFloat("_Disintegrate"), Is.Zero);
            Assert.That(response.IsReacting, Is.False);
            settings.pokeEdgeDepthPixels = 0f;
            response.Poke();
            response.Advance(.2f);
            Assert.That(material.GetFloat("_Disintegrate"), Is.Zero, "Zero edge depth disables the dissolve.");
        }

        [Test]
        public void RandomSeed_EachPokeChangesPatternButKeepsMaskAndOutlineInSync()
        {
            var root = new GameObject("Random edge", typeof(RectTransform));
            created.Add(root);
            var rect = (RectTransform)root.transform;
            rect.sizeDelta = new Vector2(400f, 200f);
            var outline = ArtifactInterfaceView.Decorate("Outline", rect, ArtifactGeometry.Shape.Frame, Color.white);
            var response = ArtifactSlimeReaction.Ensure(rect);
            var settings = ScriptableObject.CreateInstance<ArtifactInterfaceStyle>();
            created.Add(settings);
            settings.pokeNoiseSeed = 123;
            settings.pokeRandomizeSeed = true;
            Set(response, "style", settings);
            var randomState = UnityEngine.Random.state;
            try
            {
                response.SetFocused(true);
                var graphic = root.GetComponent<UnityEngine.UI.Image>();
                float first = graphic.materialForRendering.GetFloat("_NoiseSeed");
                Assert.That(first, Is.Not.EqualTo(123f));
                var band = root.transform.Find("Disintegration Edge").GetComponent<ArtifactGeometry>();
                response.Advance(settings.pokeDisintegrateSeconds);
                Assert.That(graphic.materialForRendering.GetFloat("_NoiseSeed"), Is.EqualTo(first));
                Assert.That(outline.materialForRendering.GetFloat("_NoiseSeed"), Is.EqualTo(first));
                Assert.That(band.materialForRendering.GetFloat("_NoiseSeed"), Is.EqualTo(first));
                response.Advance(settings.pokeDisintegrateSeconds + settings.pokeGlitchSeconds * .5f);
                Assert.That(graphic.materialForRendering.GetFloat("_NoiseSeed"), Is.EqualTo(first), "Restoration uses the same pattern.");
                response.SetFocused(true);
                Assert.That(graphic.materialForRendering.GetFloat("_NoiseSeed"), Is.EqualTo(first), "Held selection must not reroll.");
                response.OnPointerEnter(null);
                Assert.That(graphic.materialForRendering.GetFloat("_NoiseSeed"), Is.Not.EqualTo(first));
                Assert.That(settings.pokeNoiseSeed, Is.EqualTo(123), "Do not overwrite the saved fixed seed.");
                Assert.That(UnityEngine.Random.state, Is.EqualTo(randomState), "UI variation must not affect gameplay randomness.");
                settings.pokeRandomizeSeed = false;
                response.Poke();
                Assert.That(graphic.materialForRendering.GetFloat("_NoiseSeed"), Is.EqualTo(123f));
                Assert.That(outline.materialForRendering.GetFloat("_NoiseSeed"), Is.EqualTo(123f));
                Assert.That(band.materialForRendering.GetFloat("_NoiseSeed"), Is.EqualTo(123f));
            }
            finally { UnityEngine.Random.state = randomState; }
        }

        [Test]
        public void SlimePoke_SettlesWithoutMovingLayoutOrRepeatingWhileSelected()
        {
            var root = new GameObject("Poke test", typeof(RectTransform));
            created.Add(root);
            var rect = (RectTransform)root.transform;
            rect.anchoredPosition = new Vector2(25f, 50f);
            var outline = ArtifactInterfaceView.Decorate("Outline", rect, ArtifactGeometry.Shape.Frame, Color.white);
            var hit = new GameObject("Hit area", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            hit.transform.SetParent(rect, false);
            hit.GetComponent<UnityEngine.UI.Image>().color = Color.clear;
            var reaction = ArtifactSlimeReaction.Ensure(rect);
            Assert.That(hit.transform.parent, Is.SameAs(rect), "The pointer surface must stay outside the moving visual child.");
            var settings = ScriptableObject.CreateInstance<ArtifactInterfaceStyle>();
            created.Add(settings);
            Set(reaction, "style", settings);
            reaction.SetFocused(true);
            Assert.That(reaction.Motion.localScale, Is.EqualTo(Vector3.one));
            Assert.That(outline.ReactionAmount, Is.GreaterThan(0f));
            Assert.That(rect.anchoredPosition, Is.EqualTo(new Vector2(25f, 50f)));
            reaction.Advance(2f);
            Assert.That(outline.ReactionAmount, Is.Zero);
            Assert.That(reaction.Motion.localScale, Is.EqualTo(Vector3.one));
            Assert.That(reaction.Motion.localRotation, Is.EqualTo(Quaternion.identity));
            Assert.That(reaction.IsReacting, Is.False);
            reaction.SetFocused(true);
            Assert.That(reaction.IsReacting, Is.False, "A held selection must not continuously restart the poke.");
            reaction.OnPointerEnter(null);
            Assert.That(reaction.IsReacting, Is.True, "Re-entering an already selected item should poke it again.");
            settings.outlineRipplePixels = 0f;
            reaction.Advance(.1f);
            Assert.That(reaction.Motion.localScale, Is.EqualTo(Vector3.one));
            Assert.That(reaction.Motion.localRotation, Is.EqualTo(Quaternion.identity));
            settings.slimeResponse = false;
            Call(reaction, "LateUpdate");
            Assert.That(reaction.Motion.localScale, Is.EqualTo(Vector3.one));
            Assert.That(reaction.IsReacting, Is.False);
        }

        private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
        private static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Private).GetValue(target);
        private static void Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
    }
}
