﻿using KSP.UI.Screens;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NextStarIndustries
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    class ScienceGrabber : MonoBehaviour
    {
        //GUI
        ApplicationLauncherButton SGAppButton;

        //states
        Vessel stateVessel;
        CelestialBody stateBody;
        string stateBiome;
        ExperimentSituations stateSituation = 0;

        //thread control
        bool autoTransfer = true;
        System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();

        // to do list
        //
        // integrate science lab
        // allow a user specified container to hold data
        // transmit data from probes automaticly

        void Awake()
        {
            GameEvents.onGUIApplicationLauncherReady.Add(SetupAppButton);
        }

        void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(SetupAppButton);
            if (SGAppButton != null) ApplicationLauncher.Instance.RemoveModApplication(SGAppButton);
        }
        void SetupAppButton()
        {

            if (SGAppButton == null)
            {
                SGAppButton = ApplicationLauncher.Instance.AddModApplication(

                       onTrue: ToggleCollection,
                       onFalse: ToggleCollection,
                       onHover: null,
                       onHoverOut: null,
                       onEnable: null,
                       onDisable: null,
                       visibleInScenes: ApplicationLauncher.AppScenes.FLIGHT,
                       texture: GetIconTexture(autoTransfer)
                   );
            }

        }

        void FixedUpdate() // running in physics update so that the vessel is always in a valid state to check for science.
        {
            // this is the primary logic that controls when to do what, so we aren't contstantly eating cpu
            if (FlightGlobals.ActiveVessel.FindPartModulesImplementing<ModuleScienceContainer>().Any() == false)
            {
                // Check if any science containers are on the vessel, if not, remove the app button
                if (SGAppButton != null) ApplicationLauncher.Instance.RemoveModApplication(SGAppButton);
            }
            else if (HighLogic.CurrentGame.Mode == Game.Modes.CAREER | HighLogic.CurrentGame.Mode == Game.Modes.SCIENCE_SANDBOX) // only modes with science mechanics will run
            {
                if (SGAppButton == null) SetupAppButton();
                if (autoTransfer) // if we've enabled the app to run, on by default, the toolbar button toggles this.
                {
                    TransferScience();// always move experiment data to science container, mostly for manual experiments
                    if (StatesHaveChanged()) // if we are in a new state, we will check and run experiments
                    {
                        RunScience();
                    }
                }
            }

        }

        void TransferScience() // automaticlly find, transer and consolidate science data on the vessel
        {
            if (ActiveContainer().GetActiveVesselDataCount() != ActiveContainer().GetScienceCount()) // only actually transfer if there is data to move
            {

                Debug.Log("[ScienceGrabber!] Transfering science to container.");

                ActiveContainer().StoreData(GetExperimentList().Cast<IScienceDataContainer>().ToList(), true); // this is what actually moves the data to the active container
                var containerstotransfer = GetContainerList(); // a temporary list of our containers
                containerstotransfer.Remove(ActiveContainer()); // we need to remove the container we storing the data in because that would be wierd and buggy
                ActiveContainer().StoreData(containerstotransfer.Cast<IScienceDataContainer>().ToList(), true); // now we store all data from other containers
            }
        }

        void RunScience() // this is primary business logic for finding and running valid experiments
        {
            if (GetExperimentList() == null) // hey, it can happen!
            {
                Debug.Log("[ScienceGrabber!] There are no experiments.");
            }
            else
            {
                foreach (ModuleScienceExperiment currentExperiment in GetExperimentList()) // loop through all the experiments onboard
                {

                    Debug.Log("[ScienceGrabber!] Checking experiment: " + CurrentScienceSubject(currentExperiment.experiment).id);

                    if (ActiveContainer().HasData(NewScienceData(currentExperiment))) // skip data we already have onboard
                    {

                        Debug.Log("[ScienceGrabber!] Skipping: We already have that data onboard.");

                    }
                    else if (!SurfaceSamplesUnlocked() && currentExperiment.experiment.id == "surfaceSample") // check to see is surface samples are unlocked
                    {
                        Debug.Log("[ScienceGrabber!] Skipping: Surface Samples are not unlocked.");
                    }
                    else if (!currentExperiment.rerunnable && !IsScientistOnBoard()) // no cheating goo and materials here
                    {

                        Debug.Log("[ScienceGrabber!] Skipping: Experiment is not repeatable.");

                    }
                    else if (!currentExperiment.experiment.IsAvailableWhile(CurrentSituation(), CurrentBody())) // this experiement isn't available here so we skip it
                    {

                        Debug.Log("[ScienceGrabber!] Skipping: Experiment is not available for this situation/atmosphere.");

                    }
                    else if (CurrentScienceValue(currentExperiment) < 0.1) // this experiment has no more value so we skip it
                    {

                        Debug.Log("[ScienceGrabber!] Skipping: No more science is available: ");

                    }
                    else
                    {
                        Debug.Log("[ScienceGrabber!] Running experiment: " + CurrentScienceSubject(currentExperiment.experiment).id);

                        ActiveContainer().AddData(NewScienceData(currentExperiment)); //manually add data to avoid deployexperiment state issues
                    }

                }
            }
        }

        private bool SurfaceSamplesUnlocked() // checking that the appropriate career unlocks are flagged
        {
            return GameVariables.Instance.UnlockedEVA(ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.AstronautComplex))
                && GameVariables.Instance.UnlockedFuelTransfer(ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.ResearchAndDevelopment));
        }

        float CurrentScienceValue(ModuleScienceExperiment currentExperiment) // the ammount of science an experiment should return
        {
            return ResearchAndDevelopment.GetScienceValue(
                                    currentExperiment.experiment.baseValue * currentExperiment.experiment.dataScale,
                                    CurrentScienceSubject(currentExperiment.experiment));
        }

        ScienceData NewScienceData(ModuleScienceExperiment currentExperiment) // construct our own science data for an experiment
        {
            return new ScienceData(
                       amount: currentExperiment.experiment.baseValue * CurrentScienceSubject(currentExperiment.experiment).dataScale,
                       xmitValue: currentExperiment.xmitDataScalar,
                       xmitBonus: 0f,
                       id: CurrentScienceSubject(currentExperiment.experiment).id,
                       dataName: CurrentScienceSubject(currentExperiment.experiment).title
                       );
        }

        Vessel CurrentVessel() // dur :P
        {
            return FlightGlobals.ActiveVessel;
        }

        CelestialBody CurrentBody()
        {
            return FlightGlobals.ActiveVessel.mainBody;
        }

        ExperimentSituations CurrentSituation()
        {
            return ScienceUtil.GetExperimentSituation(FlightGlobals.ActiveVessel);
        }

        string CurrentBiome() // some crazy nonsense to get the actual biome string
        {
            if (FlightGlobals.ActiveVessel != null)
                if (FlightGlobals.ActiveVessel.mainBody.BiomeMap != null)
                    return !string.IsNullOrEmpty(FlightGlobals.ActiveVessel.landedAt)
                                    ? Vessel.GetLandedAtString(FlightGlobals.ActiveVessel.landedAt)
                                    : ScienceUtil.GetExperimentBiome(FlightGlobals.ActiveVessel.mainBody,
                                                FlightGlobals.ActiveVessel.latitude, FlightGlobals.ActiveVessel.longitude);

            return string.Empty;
        }

        ScienceSubject CurrentScienceSubject(ScienceExperiment experiment)
        {
            string fixBiome = string.Empty; // some biomes don't have 4th string, so we just put an empty in to compare strings later
            if (experiment.BiomeIsRelevantWhile(CurrentSituation())) fixBiome = CurrentBiome();// for those that do, we add it to the string
            return ResearchAndDevelopment.GetExperimentSubject(experiment, CurrentSituation(), CurrentBody(), fixBiome, null);//ikr!, we pretty much did all the work already, jeez
        }

        ModuleScienceContainer ActiveContainer() // set the container to gather all science data inside, usualy this is the root command pod of the oldest vessel
        {
            return FlightGlobals.ActiveVessel.FindPartModulesImplementing<ModuleScienceContainer>().FirstOrDefault();
        }

        List<ModuleScienceExperiment> GetExperimentList() // a list of all experiments
        {
            return FlightGlobals.ActiveVessel.FindPartModulesImplementing<ModuleScienceExperiment>();
        }

        List<ModuleScienceContainer> GetContainerList() // a list of all science containers
        {
            return FlightGlobals.ActiveVessel.FindPartModulesImplementing<ModuleScienceContainer>(); // list of all experiments onboard
        }

        bool StatesHaveChanged() // Track our vessel state, it is used for thread control to know when to fire off new experiments since there is no event for this
        {
            if (CurrentVessel() != stateVessel | CurrentSituation() != stateSituation | CurrentBody() != stateBody | CurrentBiome() != stateBiome)
            {
                stateVessel = CurrentVessel();
                stateBody = CurrentBody();
                stateSituation = CurrentSituation();
                stateBiome = CurrentBiome();
                stopwatch.Reset();
                stopwatch.Start();
                return true;
            }
            else return false;

            //if (stopwatch.ElapsedMilliseconds > 100) // throttling detection to kill transient states.
            //{
            //    stopwatch.Reset();

            //    Debug.Log("[ForScience!] Vessel in new experiment state.");

            //    return true;
            //}
            //else return false;
        }

        void ToggleCollection() // This is our main toggle for the logic and changes the icon between green and red versions on the bar when it does so.
        {
            autoTransfer = !autoTransfer;
            SGAppButton.SetTexture(GetIconTexture(autoTransfer));
        }

        bool IsScientistOnBoard() // check if there is a scientist onboard so we can rerun things like goo or scijrs
        {
            foreach (ProtoCrewMember kerbal in CurrentVessel().GetVesselCrew())
            {
                if (kerbal.experienceTrait.Title == "Scientist") return true;
            }
            return false;
        }

        Texture2D GetIconTexture(bool b) // just returns the correct icon for the given state
        {
            if (b) return GameDatabase.Instance.GetTexture("ScienceGrabber/Plugins/Icons/SG_active", false);
            else return GameDatabase.Instance.GetTexture("ScienceGrabber/Plugins/Icons/SG_inactive", false);
        }
    }
}