import {Auth} from './auth.js';
const $ = id => document.getElementById(id);
const stateLabels = {Idle:'Ready for a prompt',Stopped:'Task stopped',Completed:'Task complete',Paused:'Agent paused',PrivacyPaused:'Paused on your PC',NetworkInterrupted:'Connection interrupted',Observing:'Looking at your PC',Planning:'Planning the next step',Verifying:'Checking what changed',WaitingForLearner:'Your turn on the PC',WaitingForApplication:'Waiting for the page',Recovering:'Your agent needs attention',Acting:'Agent acting',Offline:'Computer disconnected'};
let auth, devices = [], selected = '', mode = 'guide', sending = false, refreshing = false, online = true, lastRequest = null, stopped = false;
const device = () => devices.find(d=>d.connection_id===selected);
const notice = text => { $('notice').textContent=text; $('notice').hidden=!text; };
const message = error => error instanceof TypeError || error.name === 'TimeoutError' ? 'Could not reach your agent. The command is not confirmed; reconnect and check its status.' : error.message;
async function api(path,options={}) {
  const token=await auth.access();
  const response=await fetch(`/v1/remote${path}`,{...options,cache:'no-store',signal:AbortSignal.timeout(12000),headers:{Authorization:`Bearer ${token}`,'Content-Type':'application/json',...options.headers}});
  if (!response.ok) { const body=await response.json().catch(()=>({})); if(response.status===401) throw Error('Your sign-in expired. Sign out and sign in again.'); throw Error(typeof body.detail==='string'?body.detail:'The request could not be completed.'); }
  return response.json();
}
function render() {
  const current=device(), status=current?.status, connected=Boolean(auth?.signedIn&&current?.online&&online);
  const active=Boolean(status&&!['Idle','Stopped','Completed','Offline'].includes(status.state));
  const busy=Boolean(status?.busy||current?.pending_command);
  if(active && status?.mode) mode=status.mode;
  document.body.classList.toggle('autonomous',mode==='autonomous');
  document.querySelector('meta[name=theme-color]').content=mode==='autonomous'?'#fff0f2':'#edf8f0';
  $('account').textContent=auth?.signedIn?'Sign out':'Sign in'; $('account').disabled=!auth;
  $('signin-panel').hidden=Boolean(auth?.signedIn);
  $('guided').setAttribute('aria-pressed',String(mode==='guide')); $('autonomous').setAttribute('aria-pressed',String(mode==='autonomous'));
  $('guided').disabled=$('autonomous').disabled=active||busy||sending;
  $('mode-help').textContent=mode==='guide'?'The agent guides. You click on your PC.':current&&!current.allow_autonomous?'Allow Autonomous phone tasks in Preferences on your PC first.':'The agent clicks, scrolls and types on your PC.';
  $('connection-label').textContent=!online?'Connection lost':connected?'Connected':current?'Offline':'Not connected';
  $('connection-dot').classList.toggle('online',connected);
  $('device').disabled=!auth?.signedIn||!devices.length||sending;
  $('goal').disabled=!connected||active||busy||sending;
  $('send').disabled=!connected||active||busy||sending||(mode==='autonomous'&&!current?.allow_autonomous);
  $('send').firstChild.textContent=sending?'Sending… ':'Send task ';
  $('pause').disabled=!connected||sending||(!active&&!current?.pending_command)||status?.can_resume;
  $('resume').disabled=!connected||sending||!status?.can_resume||busy;
  $('stop').disabled=!connected||sending||(!active&&!current?.pending_command&&!status?.busy);
  $('check').hidden=!(connected&&status?.can_check&&mode==='guide'); $('check').disabled=sending||busy;
  $('disconnect').disabled=!current||!auth?.signedIn||sending;
  $('task-state').querySelector('span').textContent=status?.state==='WaitingForLearner'&&status.mode==='autonomous'?'Preparing the next action':stateLabels[status?.state]||'Ready for a prompt';
  $('instruction').textContent=status?.instruction||'Your agent’s next step will appear here.';
  $('task-notice').textContent=status?.notice||''; $('task-notice').hidden=!status?.notice;
  const receipt=current?.last_command;
  $('receipt').textContent=receipt ? receipt.status==='queued'?`${receipt.kind==='start'?'Task':receipt.kind[0].toUpperCase()+receipt.kind.slice(1)} sent · waiting for your PC.`:receipt.message||`Command ${receipt.status}.` : '';
}
async function refresh() {
  if(!auth?.signedIn||refreshing||stopped)return;
  refreshing=true;
  try {
    const result=await api('/connections'); if(stopped)return; devices=result.devices; online=true;
    if(!devices.some(d=>d.connection_id===selected)) selected=(devices.find(d=>d.online)||devices[0])?.connection_id||'';
    const ids=devices.map(d=>`${d.connection_id}:${d.label}:${d.online}`).join('|');
    if($('device').dataset.contents!==ids){$('device').replaceChildren(); for(const item of devices){const option=document.createElement('option'); option.value=item.connection_id;option.textContent=item.label;$('device').append(option);} if(!devices.length){const option=document.createElement('option');option.textContent='No computer connected';$('device').append(option);} $('device').dataset.contents=ids;}
    $('device').value=selected;
  }catch(error){online=false;notice(message(error));}finally{refreshing=false;render();}
}
async function send(kind) {
  const current=device(); if(!current||sending)return;
  const payload={schema_version:1,kind,...(kind==='start'?{goal:$('goal').value.trim(),mode}:{})};
  if(kind==='start'&&!payload.goal){$('goal').focus();return;}
  const signature=JSON.stringify({connection:current.connection_id,...payload});
  if(lastRequest?.signature!==signature) lastRequest={signature,id:crypto.randomUUID()};
  payload.request_id=lastRequest.id; sending=true;notice('');render();
  try {const receipt=await api(`/connections/${current.connection_id}/commands`,{method:'POST',body:JSON.stringify(payload)});current.last_command=receipt;current.pending_command=receipt.status==='queued'?kind:null;lastRequest=null;}
  catch(error){notice(message(error));}finally{sending=false;render();await refresh();}
}
$('task-form').addEventListener('submit',event=>{event.preventDefault();void send('start');});
for(const kind of ['pause','resume','stop','check'])$(kind).addEventListener('click',()=>void send(kind));
for(const value of ['guided','autonomous'])$(value).addEventListener('click',()=>{mode=value==='guided'?'guide':'autonomous';render();});
$('device').addEventListener('change',()=>{selected=$('device').value;notice('');render();});
$('connect-help').addEventListener('click',()=>{const open=$('connection-help').hidden;$('connection-help').hidden=!open;$('connect-help').setAttribute('aria-expanded',String(open));});
$('disconnect').addEventListener('click',async()=>{const current=device();if(!current)return;sending=true;render();try{await api(`/connections/${current.connection_id}`,{method:'DELETE'});notice('Disconnected. Your PC will pause its remote task when it receives the disconnect.');}catch(error){notice(message(error));}finally{sending=false;await refresh();}});
async function account(){try{if(auth?.signedIn){stopped=true;devices=[];render();await auth.signOut();}else await auth?.signIn();}catch(error){notice(message(error));}}
$('account').addEventListener('click',()=>void account());$('signin').addEventListener('click',()=>void account());
window.addEventListener('online',()=>void refresh());window.addEventListener('offline',()=>{online=false;notice('This phone is offline. Tasks already running on your PC may continue.');render();});
document.addEventListener('visibilitychange',()=>{if(!document.hidden)void refresh();});
try {
  const response=await fetch('/control/config.json',{cache:'no-store'});if(!response.ok)throw Error('Sign-in configuration is unavailable.');const config=await response.json();
  auth=new Auth(config);if(!config.configured)throw Error('Sign-in configuration is unavailable.');await auth.complete();render();await refresh();
}catch(error){notice(message(error));render();}
setInterval(()=>{if(!document.hidden)void refresh();},4000);
