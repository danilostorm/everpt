const form = document.getElementById('register');
const submit = document.getElementById('submit');
const message = document.getElementById('message');
const errors = {
    invalid_request: 'Dados inválidos. Confira os campos.',
    invalid_username: 'Use de 3 a 20 letras, números ou underline (_).',
    invalid_password: 'A senha deve ter de 8 a 20 caracteres.',
    password_mismatch: 'As senhas informadas não são iguais.',
    username_taken: 'Esse nome de usuário já está em uso.',
    registration_unavailable: 'O cadastro está indisponível no momento.'
};
form.addEventListener('submit', async event => {
    event.preventDefault();
    message.classList.remove('success');
    message.textContent = '';
    if (!form.reportValidity()) return;
    const username = document.getElementById('username').value.trim();
    const password = document.getElementById('password').value;
    const confirmPassword = document.getElementById('confirm').value;
    if (password !== confirmPassword) {
        message.textContent = errors.password_mismatch;
        return;
    }
    submit.disabled = true;
    try {
        const response = await fetch('/api/register', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                username, password, confirmPassword,
                website: document.getElementById('website').value
            }),
            credentials: 'omit',
            cache: 'no-store'
        });
        if (response.status === 429) {
            message.textContent = 'Muitas tentativas. Aguarde alguns minutos.';
            return;
        }
        const data = await response.json().catch(() => ({}));
        if (!response.ok || !data.ok) {
            message.textContent = errors[data.error] ?? 'Não foi possível cadastrar. Tente novamente.';
            return;
        }
        document.getElementById('password').value = '';
        document.getElementById('confirm').value = '';
        message.classList.add('success');
        message.textContent = 'Conta criada! Você já pode entrar no EverPT usando seu usuário e sua senha.';
        form.querySelectorAll('input').forEach(input => { input.disabled = true; });
        submit.textContent = 'Conta criada';
    } catch {
        message.textContent = 'Sem conexão com o servidor. Tente novamente.';
    } finally {
        if (!message.classList.contains('success')) submit.disabled = false;
    }
});
