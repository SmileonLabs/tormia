using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace Tormia.Ontology.Core
{
    [DisallowMultipleComponent] public sealed class OntologyAccountWorldCreationPanel : MonoBehaviour
    {
        [SerializeField] CanvasGroup panelGroup; [SerializeField] TMP_InputField worldNameInput; [SerializeField] TMP_Text worldNameLabel; [SerializeField] Button createButton; [SerializeField] Button backButton;
        OntologyWorldAuthorityAccountEntryFlow entryFlow; OntologyAccountFlowNavigator navigator; bool creating;
        void Awake(){ Resolve(); Bind(); Close(); } void OnEnable(){OntologyLanguagePackService.LanguageChanged+=Localize;Localize();} void OnDisable(){OntologyLanguagePackService.LanguageChanged-=Localize;} public void Open(){ Resolve(); Localize(); panelGroup.alpha=1; panelGroup.interactable=panelGroup.blocksRaycasts=true; worldNameInput?.ActivateInputField(); } public void Close(){ if(panelGroup==null)return; panelGroup.alpha=0; panelGroup.interactable=panelGroup.blocksRaycasts=false; }
        void Bind(){ if(createButton!=null){createButton.onClick.RemoveAllListeners();createButton.onClick.AddListener(Create);} if(backButton!=null){backButton.onClick.RemoveAllListeners();backButton.onClick.AddListener(()=>navigator?.ShowWorldSelection());} }
        void Create(){ if(creating)return; creating=true; entryFlow?.CreateWorld(worldNameInput?.text,null,"private",ok=>{creating=false;if(ok)navigator?.ShowWorldSelection();});}
        void Resolve(){ entryFlow ??= FindAnyObjectByType<OntologyWorldAuthorityAccountEntryFlow>(); navigator ??= FindAnyObjectByType<OntologyAccountFlowNavigator>(); panelGroup ??= GetComponent<CanvasGroup>(); worldNameInput ??= GetComponentInChildren<TMP_InputField>(true); if(worldNameInput!=null&&worldNameInput.textComponent==null)worldNameInput.textComponent=worldNameInput.GetComponentInChildren<TMP_Text>(true); createButton ??= transform.Find("ContinueToWorldButton")?.GetComponent<Button>(); backButton ??= transform.Find("BackButton")?.GetComponent<Button>();}
        void Localize(){var h=transform.Find("Text (TMP)")?.GetComponent<TMP_Text>();if(h!=null)h.text="<size=38>"+L("ui.account.world_create.title","CREATE WORLD")+"</size>\n<size=19>"+L("ui.account.world_create.prompt","Give your new world a name.")+"</size>"; if(worldNameLabel!=null)worldNameLabel.text=L("ui.account.world_create.name","WORLD NAME"); if(worldNameInput?.placeholder is TMP_Text placeholder)placeholder.text=L("ui.account.world_create.name_placeholder","Name your new world"); SetButton(createButton,L("ui.account.world_create.create","CREATE"));SetButton(backButton,L("ui.account.back","BACK"));}
        static string L(string key,string fallback)=>OntologyLanguagePackService.Text(key,fallback); static void SetButton(Button b,string v){var t=b?.GetComponentInChildren<TMP_Text>(true);if(t!=null)t.text=v;}
    }
}
