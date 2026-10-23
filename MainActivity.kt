// =====================================================================
// ERP FERKAUTOPARTS - App Mobile (Kotlin / Android)
// Como usar: no Android Studio crie um projeto "Empty Views Activity"
// (package com.ferkautoparts.mobile, linguagem Kotlin, Min SDK 24),
// substitua o MainActivity.kt por este arquivo e adicione no
// AndroidManifest.xml, acima de <application>:
//   <uses-permission android:name="android.permission.INTERNET" />
// Não precisa de nenhuma biblioteca extra.
// =====================================================================
package com.ferkautoparts.mobile

import android.graphics.Color
import android.os.Bundle
import android.text.InputType
import android.view.Gravity
import android.widget.*
import androidx.appcompat.app.AppCompatActivity
import org.json.JSONArray
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL
import kotlin.concurrent.thread

class MainActivity : AppCompatActivity() {

    // >>> TROQUE pela URL da sua API no Render <<<
    private val apiBase = "https://erp-ferkautoparts.onrender.com"
    private var token: String? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        telaLogin()
    }

    // ---------------- TELA 1: LOGIN ----------------
    private fun telaLogin() {
        val layout = coluna()
        layout.addView(titulo("FERKAUTOPARTS"))
        val login = EditText(this).apply { hint = "Usuário" }
        val senha = EditText(this).apply {
            hint = "Senha"
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_PASSWORD
        }
        val status = TextView(this).apply { setTextColor(Color.RED) }
        val btn = Button(this).apply { text = "Entrar" }
        btn.setOnClickListener {
            status.text = "Entrando..."
            thread {
                try {
                    val corpo = JSONObject().put("login", login.text.toString()).put("senha", senha.text.toString())
                    val resp = JSONObject(http("POST", "/api/auth/login", corpo.toString()))
                    token = resp.getString("token")
                    runOnUiThread { telaProdutos() }
                } catch (e: Exception) {
                    runOnUiThread { status.text = "Usuário ou senha inválidos" }
                }
            }
        }
        listOf(login, senha, btn, status).forEach { layout.addView(it) }
        setContentView(layout)
    }

    // ---------------- TELA 2: ESTOQUE ----------------
    private fun telaProdutos() {
        val layout = coluna()
        layout.addView(titulo("Estoque"))
        val lista = ListView(this)
        layout.addView(lista, LinearLayout.LayoutParams(-1, 0, 1f))
        val btnAtualizar = Button(this).apply { text = "Atualizar" }
        layout.addView(btnAtualizar)
        setContentView(layout)

        fun carregar() = thread {
            try {
                val arr = JSONArray(http("GET", "/api/produtos"))
                val itens = (0 until arr.length()).map {
                    val p = arr.getJSONObject(it)
                    "${p.getString("codigo")} - ${p.getString("nome")}\n" +
                    "Estoque: ${p.getInt("estoque")}  |  Status: ${p.getString("status")}"
                }
                runOnUiThread {
                    lista.adapter = ArrayAdapter(this, android.R.layout.simple_list_item_1, itens)
                }
            } catch (e: Exception) {
                runOnUiThread { Toast.makeText(this, "Erro: ${e.message}", Toast.LENGTH_LONG).show() }
            }
        }
        btnAtualizar.setOnClickListener { carregar() }
        carregar()
    }

    // ---------------- HTTP (consome a API REST) ----------------
    private fun http(metodo: String, rota: String, corpo: String? = null): String {
        val con = URL(apiBase + rota).openConnection() as HttpURLConnection
        con.requestMethod = metodo
        con.connectTimeout = 60000   // Render gratuito "acorda" em ~50s
        con.readTimeout = 60000
        con.setRequestProperty("Content-Type", "application/json")
        token?.let { con.setRequestProperty("Authorization", "Bearer $it") }
        if (corpo != null) {
            con.doOutput = true
            con.outputStream.use { it.write(corpo.toByteArray()) }
        }
        if (con.responseCode !in 200..299) throw Exception("HTTP ${con.responseCode}")
        return con.inputStream.bufferedReader().readText()
    }

    // ---------------- helpers de layout ----------------
    private fun coluna() = LinearLayout(this).apply {
        orientation = LinearLayout.VERTICAL
        setPadding(48, 96, 48, 48)
    }
    private fun titulo(t: String) = TextView(this).apply {
        text = t; textSize = 24f; gravity = Gravity.CENTER; setPadding(0, 0, 0, 48)
    }
}
