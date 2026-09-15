package com.tgspecialwatch.client

import org.json.JSONObject

data class SocketEnvelope(
    val type: String,
    val id: String? = null,
    val source: String? = null,
    val preview: String? = null,
    val rule: String? = null,
    val level: String? = null,
    val at: String? = null
) {
    companion object {
        fun parse(line: String): SocketEnvelope? {
            return try {
                val o = JSONObject(line)
                SocketEnvelope(
                    type = o.optString("type"),
                    id = o.optString("id").ifBlank { null },
                    source = o.optString("source").ifBlank { null },
                    preview = o.optString("preview").ifBlank { null },
                    rule = o.optString("rule").ifBlank { null },
                    level = o.optString("level").ifBlank { null },
                    at = o.optString("at").ifBlank { null }
                )
            } catch (_: Exception) {
                null
            }
        }

        fun ack(id: String): String =
            JSONObject().put("type", "ack").put("id", id).toString()

        fun hello(): String =
            JSONObject().put("type", "hello").put("client", "android").toString()

        fun ping(): String =
            JSONObject().put("type", "ping").toString()
    }
}
